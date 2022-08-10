// -----------------------------------------------------------------------
// <copyright file="DiagnosticAnalysisLauncher.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DiagnosticAnalysisLauncher
{
    internal class DiagnosticAnalysisLauncher
    {
        private const int MaxProcessorTime = 300;
        private const int MaxPrivateBytes = 800 * 1024 * 1024;
        private readonly string _dumpFile;
        private readonly string _outputFolder;

        public DiagnosticAnalysisLauncher(string dumpFile, string outputFolder)
        {
            _dumpFile = dumpFile;
            _outputFolder = outputFolder;
        }

        internal void AnalyzeDump()
        {
            Logger.Init(string.Empty, string.Empty, "DiagnosticAnalysisLauncher", false);

            if (!IsStampEnabled()
                || !IsSiteEnabled()
                || !GetAnalysisExePath(out string diagnosticAnalysisExePath)
                || !File.Exists(_dumpFile))
            {
                return;
            }

            AnalyzeMemoryDump(diagnosticAnalysisExePath, _dumpFile);
        }

        private bool IsSiteEnabled()
        {
            string val = Environment.GetEnvironmentVariable("WEBSITE_ENABLE_DIAGNOSTIC_ANALYSIS_DAAS");
            if (!string.IsNullOrWhiteSpace(val))
            {
                return val.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private bool IsStampEnabled()
        {
            try
            {
                var scmHostingConfigPath = Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\\SiteExtensions\\kudu\\ScmHostingConfigurations.txt");
                if (!File.Exists(scmHostingConfigPath))
                {
                    return true;
                }

                string fileContents = File.ReadAllText(scmHostingConfigPath);
                if (!string.IsNullOrWhiteSpace(fileContents) && fileContents.Contains("RunDiagnosticAnalysisInDaaS=0"))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Exception while checking ScmHostingConfigurations", ex);
            }

            return true;
        }

        private bool GetAnalysisExePath(out string diagnosticAnalysisExePath)
        {
            diagnosticAnalysisExePath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "DiagnosticAnalysis",
                "DiagnosticAnalysis.exe");
            if (!File.Exists(diagnosticAnalysisExePath))
            {
                Logger.LogDiagnoserVerboseEvent($"GetAnalysisExePath = false for {diagnosticAnalysisExePath}");
                return false;
            }

            Logger.LogDiagnoserVerboseEvent($"GetAnalysisExePath = true");
            return true;
        }

        private void AnalyzeMemoryDump(string diagnosticAnalysisExePath, string dumpFileName)
        {
            string dumpName = Path.GetFileName(dumpFileName);
            var outputBuilder = new StringBuilder();
            var diagnosticsAnalysis = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = diagnosticAnalysisExePath,
                    Arguments = dumpFileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
                EnableRaisingEvents = true
            };

            diagnosticsAnalysis.OutputDataReceived += new DataReceivedEventHandler
            (
                delegate (object sender, DataReceivedEventArgs e)
                {
                    outputBuilder.Append(e.Data);
                }
            );


            Stopwatch watch = new Stopwatch();
            watch.Start();
            diagnosticsAnalysis.Start();
            diagnosticsAnalysis.BeginOutputReadLine();

            double UserProcessorTime = 0;
            double PrivilegedProcessorTime = 0;
            double TotalProcessorTime = 0;
            long PrivateMemorySize64 = 0;

            while (!diagnosticsAnalysis.HasExited)
            {
                Thread.Sleep(1000);

                try
                {
                    diagnosticsAnalysis.Refresh();
                    UserProcessorTime = diagnosticsAnalysis.UserProcessorTime.TotalSeconds;
                    PrivilegedProcessorTime = diagnosticsAnalysis.PrivilegedProcessorTime.TotalSeconds;
                    TotalProcessorTime = diagnosticsAnalysis.TotalProcessorTime.TotalSeconds;
                    PrivateMemorySize64 = diagnosticsAnalysis.PrivateMemorySize64;

                    if (TotalProcessorTime > MaxProcessorTime || PrivateMemorySize64 > MaxPrivateBytes)
                    {
                        Logger.LogDiagnoserWarningEvent(
                            "Killing DiagnosticAnalysis.exe due to high resource consumption",
                            $"{TotalProcessorTime} seconds of CPU time and {PrivateMemorySize64 / (1024 * 1024)} MB of memory");
                        diagnosticsAnalysis.SafeKillProcess();
                        break;
                    }
                }
                catch (Exception)
                {
                }
            }

            diagnosticsAnalysis.WaitForExit();
            diagnosticsAnalysis.CancelOutputRead();
            watch.Stop();

            var diagCliJsonOutput = outputBuilder.ToString();

            try
            {
                if (!string.IsNullOrWhiteSpace(diagCliJsonOutput))
                {
                    var diagCliOutputFileName = CreateDiagCliJson(dumpFileName, diagCliJsonOutput);
                    CreateReportHtml(diagCliOutputFileName, $"{Path.GetFileNameWithoutExtension(dumpFileName)}.html");
                }

                var analysis = JsonConvert.DeserializeObject<DiagnosticAnalysis>(diagCliJsonOutput);
                Logger.LogDiagnoserEvent(JsonConvert.SerializeObject(new { Dump = dumpName, Analysis = analysis, JsonOutputLength = diagCliJsonOutput.Length }));
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Error while parsing output for {dumpName}", ex);
            }

            var stats = new
            {
                Dump = dumpName,
                watch.Elapsed.TotalSeconds,
                UserProcessorTime,
                PrivilegedProcessorTime,
                TotalProcessorTime,
                PrivateMemorySize64
            };

            Logger.LogDiagnoserEvent(JsonConvert.SerializeObject(stats));
        }

        private string CreateDiagCliJson(string dumpFileName, string diagCliOutput)
        {
            //
            // Make sure that the Diag CLI.json file is in a child path of the
            // reports folder. This ensures that the link to this file is not
            // created in the session.
            //

            Logger.LogDiagnoserVerboseEvent("Inside CreateDiagCliJson method");

            string machineName = GetMachineNameFromDumpFileName(dumpFileName);
            string directoryName = Path.Combine(_outputFolder, machineName);
            FileSystemHelpers.EnsureDirectory(directoryName);

            var diagCliOutputFileName = Path.Combine(directoryName, "DiagCli.json");
            FileSystemHelpers.WriteAllText(diagCliOutputFileName, diagCliOutput);

            Logger.LogDiagnoserVerboseEvent($"Created {diagCliOutputFileName}");

            return diagCliOutputFileName;
        }

        private string GetMachineNameFromDumpFileName(string dumpFileName)
        {
            string fileName = Path.GetFileNameWithoutExtension(dumpFileName);
            if (fileName.Contains("_"))
            {
                return fileName.Split('_')[0];
            }

            return fileName;
        }

        private void CreateReportHtml(string jsonFilePath, string outputFileName)
        {
            Logger.LogDiagnoserVerboseEvent("Creating report file");

            string diagAnalysisDirectory = GetDiagnosticAnalysisDirectory();

            string joinResultsViewerPath = Path.Combine(diagAnalysisDirectory, "scripts", "Join-ResultsViewer.ps1");
            string resultsViewerPath = Path.Combine(diagAnalysisDirectory, "ResultsViewer.html");
            string outputFile = Path.Combine(_outputFolder, $"DiagnosticAnalysis-{outputFileName}");

            if (RunPowershellScript(
                scriptPath: joinResultsViewerPath,
                timeout: TimeSpan.FromSeconds(60),
                $@"-ResultsViewHtml ""{resultsViewerPath}""",
                $@"-ResultsJson ""{jsonFilePath}""",
                $@"-OutputFile ""{outputFile}"""
            ))
            {
                Logger.LogDiagnoserVerboseEvent($"Created report file at {outputFile}");
            }
            else
            {
                Logger.LogDiagnoserErrorEvent($"Failed to create report file at {outputFile}", string.Empty);
            }
        }

        private static string GetDiagnosticAnalysisDirectory()
        {
            // The app is in bin/DiagnosticTools/DiagnosticAnalysisLauncher.exe
            // We need to return bin/DiagnosticAnalysis

            var binDir = Path.GetDirectoryName(Path.GetDirectoryName(typeof(DiagnosticAnalysisLauncher).Assembly.Location));
            return Path.Combine(binDir, "DiagnosticAnalysis");
        }

        private bool RunPowershellScript(string scriptPath, TimeSpan timeout, params string[] arguments)
        {
            using (var psProcess = new Process())
            {
                psProcess.StartInfo = new ProcessStartInfo("PowerShell.exe", $@"-ExecutionPolicy RemoteSigned -File ""{scriptPath}"" " + string.Join(" ", arguments))
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                psProcess.OutputDataReceived += (o, a) =>
                {
                    outputBuilder.AppendLine(a.Data ?? string.Empty);
                };
                psProcess.ErrorDataReceived += (o, a) =>
                {
                    errorBuilder.AppendLine(a.Data ?? string.Empty);
                };

                psProcess.Start();

                psProcess.BeginOutputReadLine();
                psProcess.BeginErrorReadLine();

                if (!psProcess.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    Logger.LogDiagnoserErrorEvent("The script did not complete in the allotted time. The process will be killed.", string.Empty);
                    try
                    {
                        psProcess.Kill();
                    }
                    catch { }
                }

                psProcess.WaitForExit();

                if (psProcess.ExitCode == 0)
                {
                    Logger.LogDiagnoserVerboseEvent($"The script terminated successfully.");
                    Logger.LogDiagnoserVerboseEvent($"Script stdout: {outputBuilder.ToString()}");
                    Logger.LogDiagnoserVerboseEvent($"Script stderr: {errorBuilder}");
                }
                else
                {
                    Logger.LogDiagnoserErrorEvent($"The script exit code was {psProcess.ExitCode}", string.Empty);
                    Logger.LogDiagnoserErrorEvent($"Script stdout: {outputBuilder.ToString()}", string.Empty);
                    Logger.LogDiagnoserErrorEvent($"Script stderr: {errorBuilder.ToString()}", string.Empty);
                }

                return psProcess.ExitCode == 0;
            }
        }
    }
}
