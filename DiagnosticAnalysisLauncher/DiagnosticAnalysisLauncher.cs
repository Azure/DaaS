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
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;

namespace DiagnosticAnalysisLauncher
{
    internal class DiagnosticAnalysisLauncher
    {
        private readonly string _dumpFile;

        public DiagnosticAnalysisLauncher(string dumpFile)
        {
            _dumpFile = dumpFile;
        }

        internal void AnalyzeDump()
        {
            Logger.Init(string.Empty, string.Empty, "DiagnosticAnalysisLauncher", false);

            if (!IsStampEnabled()
                || !GetAnalysisExePath(out string diagnosticAnalysisExePath)
                || !File.Exists(_dumpFile))
            {
                return;
            }

            AnalyzeMemoryDump(diagnosticAnalysisExePath, _dumpFile);
        }

        private bool IsStampEnabled()
        {
            try
            {
                var scmHostingConfigPath = Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\\SiteExtensions\\kudu\\ScmHostingConfigurations.txt");
                if (!File.Exists(scmHostingConfigPath))
                {
                    return false;
                }

                string fileContents = File.ReadAllText(scmHostingConfigPath);
                if (!string.IsNullOrWhiteSpace(fileContents) && fileContents.Contains("RunDiagnosticAnalysisInDaaS=1"))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Exception while checking ScmHostingConfigurations", ex);
            }

            return false;
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

        private void AnalyzeMemoryDump(string diagnosticAnalysisExePath, string fullName)
        {
            string dumpName = Path.GetFileName(fullName);
            var outputBuilder = new StringBuilder();
            var diagnosticsAnalysis = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = diagnosticAnalysisExePath,
                    Arguments = fullName,
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
                }
                catch(Exception)
                {
                }
            }

            diagnosticsAnalysis.WaitForExit();
            diagnosticsAnalysis.CancelOutputRead();
            watch.Stop();

            var output = outputBuilder.ToString();
            try
            {
                var analysis = JsonConvert.DeserializeObject<DiagnosticAnalysis>(output);
                Logger.LogDiagnoserEvent(JsonConvert.SerializeObject(new { Dump = dumpName, Analysis = analysis }));
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
    }
}
