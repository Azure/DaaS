// -----------------------------------------------------------------------
// <copyright file="DiagnosticAnalysisLauncher.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS;
using DaaS.Configuration;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

        [DllImport("picohelper.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetSandboxProperty(
           string propertyId,
           byte[] valueBuffer,
           int valueBufferLength,
           uint flags,
           ref int copiedBytes);

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

            var arguments = new List<string>
            {
                $@"-ResultsViewHtml ""{resultsViewerPath}""",
                $@"-ResultsJson ""{jsonFilePath}""",
                $@"-OutputFile ""{outputFile}"""
            };

            string azureStorageResourceId = Settings.Instance.AccountResourceId;
            if (!string.IsNullOrWhiteSpace(azureStorageResourceId))
            {
                var azureBlobUri = GetAzureStorageBlobUri();
                if (!string.IsNullOrWhiteSpace(azureBlobUri))
                {
                    arguments.Add($@"-AzureResourceId ""{azureStorageResourceId}""");
                    arguments.Add($@"-AzureBlobUri ""{azureBlobUri}""");
                }
            }

            if (RunPowershellScript(
                scriptPath: joinResultsViewerPath,
                timeout: TimeSpan.FromSeconds(60),
                arguments.ToArray()
            ))
            {
                Logger.LogDiagnoserVerboseEvent($"Created report file at {outputFile}");
            }
            else
            {
                Logger.LogDiagnoserErrorEvent($"Failed to create report file at {outputFile}", string.Empty);
            }
        }

        private string GetAzureStorageBlobUri()
        {
            string accountAndContainerUri;
            var sasUri = Settings.Instance.AccountSasUri;
            if (string.IsNullOrWhiteSpace(sasUri))
            {
                accountAndContainerUri = GetAccountAndContainerUriFromConnectionString();
                if (string.IsNullOrWhiteSpace(accountAndContainerUri))
                {
                    return string.Empty;
                }
            }
            else
            {
                /*  The SAS Uri format:
                    https://<account host name>/<container name>?<sas params>
                The dump file path format:
                    <LogsTempDir>\<dump file relative path>
                The Azure storage blob uri should be:
                    https://<account host name>/<container name>/<app host name>/<dump file relative path>
                */

                // accountAndContainerUri = 'https://<account host name>/<container name>'
                var splitArray = sasUri.Split('?');
                if (splitArray.Length <= 0)
                {
                    return string.Empty;
                }

                accountAndContainerUri = splitArray[0]; // if there's no '?', we'll just use the entire string
            }

            var tempDir = DaasDirectory.LogsTempDir;
            if (!_dumpFile.StartsWith(tempDir))
            {
                // the dump needs to be stored in the temp directory;
                return string.Empty;
            }

            var dumpRelativePath = _dumpFile.Substring(tempDir.Length + 1 /* trim the leading '\' too */);
            if (string.IsNullOrWhiteSpace(dumpRelativePath))
            {
                return string.Empty;
            }

            return $"{accountAndContainerUri}/{Settings.Instance.DefaultHostName}/{dumpRelativePath}".Replace('\\', '/');
        }

        private static string GetAccountAndContainerUriFromConnectionString()
        {
            try
            {
                var connectionString = Settings.Instance.StorageConnectionString;
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    return string.Empty;
                }

                var storageAccount = CloudStorageAccount.Parse(connectionString);
                return storageAccount.BlobEndpoint.ToString() + "memorydumps";
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserWarningEvent("Unhandled exception parsing connection string", ex);
            }

            return string.Empty;
        }

        private static string GetDiagnosticAnalysisDirectory()
        {
            // The app is in <ROOT>/bin/DiagnosticTools/DiagnosticAnalysisLauncher.exe
            // We need to return <ROOT>/DiagnosticAnalysis

            var rootDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(typeof(DiagnosticAnalysisLauncher).Assembly.Location)));
            return Path.Combine(rootDir, "DiagnosticAnalysis");
        }

        private bool RunPowershellScript(string scriptPath, TimeSpan timeout, string[] arguments)
        {
            using (var psProcess = new Process())
            {
                string powerShellArgs = $@"-ExecutionPolicy RemoteSigned -File ""{scriptPath}"" " + string.Join(" ", arguments);
                Logger.LogDiagnoserVerboseEvent($"Launching Powershell.exe {powerShellArgs}");
                psProcess.StartInfo = new ProcessStartInfo("PowerShell.exe", powerShellArgs)
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
                    Logger.LogDiagnoserVerboseEvent($"Script stdout: {outputBuilder}");
                    Logger.LogDiagnoserVerboseEvent($"Script stderr: {errorBuilder}");
                }
                else
                {
                    Logger.LogDiagnoserErrorEvent($"The script exit code was {psProcess.ExitCode}", string.Empty);
                    Logger.LogDiagnoserErrorEvent($"Script stdout: {outputBuilder}", string.Empty);
                    Logger.LogDiagnoserErrorEvent($"Script stderr: {errorBuilder}", string.Empty);
                }

                return psProcess.ExitCode == 0;
            }
        }

        private static string GetSandboxProperty(string propertyName)
        {
            int copiedBytes = 0;
            byte[] valueBuffer = new byte[4096];
            if (GetSandboxProperty(propertyName, valueBuffer, valueBuffer.Length, 0, ref copiedBytes))
            {
                string value = Encoding.Unicode.GetString(valueBuffer, 0, copiedBytes);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
