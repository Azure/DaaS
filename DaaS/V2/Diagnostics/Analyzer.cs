// -----------------------------------------------------------------------
// <copyright file="Analyzer.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS.V2
{
    internal class Analyzer : DiagnosticTool
    {
        public override string Name { get; internal set; }
        public string Command { get; set; }
        public string Arguments { get; set; }

        internal Analyzer(Diagnoser diagnoser)
        {
            Name = diagnoser.Name;
            Command = diagnoser.Analyzer.Command;
            Arguments = diagnoser.Analyzer.Arguments;
        }

        internal async Task AnalyzeLogsAsync(List<LogFile> logs, Session activeSession, CancellationToken token)
        {
            try
            {
                Logger.LogSessionVerboseEvent($"Going to analyze logs for session. File count = {logs.Count}", activeSession.SessionId);
                if (logs.Count == 0)
                {
                    Logger.LogSessionVerboseEvent($"Found no logs to analyze", activeSession.SessionId);
                    return;
                }

                foreach (var log in logs)
                {
                    string tempOutputDir = log.GetReportTempPath(activeSession.SessionId);

                    //
                    // If DaasRunner restarts, there may be left over files from the previous runs
                    // Clean any existing files to keep the analysis output clean
                    //

                    if (FileSystemHelpers.DirectoryExists(tempOutputDir))
                    {
                        FileSystemHelpers.DeleteDirectoryContentsSafe(tempOutputDir);
                    }

                    var args = ExpandVariablesInArgument(log, tempOutputDir);

                    CancellationTokenSource analyzerTimeoutCts = new CancellationTokenSource();
                    analyzerTimeoutCts.CancelAfter(TimeSpan.FromMinutes(Infrastructure.Settings.MaxAnalyzerTimeInMinutes));
                    var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token, analyzerTimeoutCts.Token);

                    await Task.Run(() => RunProcessAsync(Command, args, activeSession.SessionId, activeSession.Description, combinedCancellationSource.Token), combinedCancellationSource.Token);
                }

                AppendReportsToLogs(activeSession, logs);

                await CopyReportsToPermanentLocationAsync(logs, activeSession);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Exception in AnalyzeLogsAsync", ex, activeSession.SessionId);
                var currentInstance = activeSession.GetCurrentInstance();
                if (currentInstance != null)
                {
                    currentInstance.AnalyzerErrors.Add($"{ex.GetType()}:{ex.Message}");
                }
            }
        }

        private string ExpandVariablesInArgument(LogFile log, string tempOutputDir)
        {
            var variables = new Dictionary<string, string>()
            {
                {"logFile", log.TempPath},
                {"outputDir", tempOutputDir}
            };
            var args = ExpandVariables(Arguments, variables);
            return args;
        }

        private void AppendReportsToLogs(Session activeSession, List<LogFile> logs)
        {
            foreach (var log in logs)
            {
                string tempOutputDir = log.GetReportTempPath(activeSession.SessionId);
                if (!Directory.Exists(tempOutputDir))
                {
                    return;
                }

                var reportDirectory = new DirectoryInfo(tempOutputDir);
                foreach (var file in reportDirectory.GetFiles("*", SearchOption.AllDirectories))
                {
                    log.Reports.Add(new Report()
                    {
                        Name = file.Name,
                        TempPath = file.FullName
                    });
                }
            }
        }

        private async Task CopyReportsToPermanentLocationAsync(List<LogFile> logs, Session activeSession)
        {
            foreach (var log in logs)
            {
                foreach (var report in log.Reports)
                {
                    string reportRelativePath = report.TempPath.Replace(DaasDirectory.ReportsTempDir + "\\", "");
                    report.PartialPath = ConvertBackSlashesToForwardSlashes(reportRelativePath, DaasDirectory.ReportsDirRelativePath);
                    report.RelativePath = $"{Utility.GetScmHostName()}/api/vfs/{report.PartialPath}";
                    string destination = Path.Combine(DaasDirectory.ReportsDir, reportRelativePath);

                    try
                    {
                        await MoveFileAsync(report.TempPath, destination, activeSession.SessionId, deleteAfterCopy: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogSessionErrorEvent($"Failed while copying {reportRelativePath} to permanent storage", ex, activeSession.SessionId);
                    }
                }

                //
                // Delete the log file now from the temp directory to save temp disk space
                //

                FileSystemHelpers.DeleteFileSafe(log.TempPath);
            }
        }
    }
}
