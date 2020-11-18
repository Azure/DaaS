//-----------------------------------------------------------------------
// <copyright file="MonitoringAnalysisController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DaaS.Configuration;
using DaaS.Storage;

namespace DaaS
{
    public class MonitoringAnalysisController
    {
        const int MAX_ANALYSIS_RETRY_COUNT = 2;
        const int ANALYSIS_HEARTBEAT_EXPIRATION_IN_MINUTES = 2;
        const int MAX_ANALYSISTIME_ON_SAME_INSTANCE = 5;

        public static string GetAnalysisFolderPath(out bool errorEncountered)
        {
            errorEncountered = false;
            string path = Path.Combine(Settings.UserSiteStorageDirectory, MonitoringSessionController.GetCpuMonitoringPath(), MonitoringSessionDirectories.Analysis);
            try
            {
                FileSystemHelpers.CreateDirectoryIfNotExists(path);
            }
            catch (Exception ex)
            {
                errorEncountered = true;
                Logger.LogCpuMonitoringErrorEvent("Failure in GetAnalysisFolderPath", ex, string.Empty);
            }
            return path;
        }

        public static void DequeueAnalysisRequest()
        {
            string analysisFolderPath = GetAnalysisFolderPath(out bool errorEncountered);

            if (!errorEncountered)
            {
                try
                {
                    string requestFile = FileSystemHelpers.GetFilesInDirectory(analysisFolderPath, "*.request", false, SearchOption.TopDirectoryOnly).FirstOrDefault();

                    if (requestFile != null)
                    {
                        var analysisRequest = FileSystemHelpers.FromJsonFile<AnalysisRequest>(requestFile);
                        var isRequestFileFromSameInstance = analysisRequest.LogFileName.StartsWith(Environment.MachineName);
                        var exceededAnalysisTimelimit = analysisRequest.StartTime != null && DateTime.UtcNow.Subtract(analysisRequest.StartTime).TotalMinutes > MAX_ANALYSISTIME_ON_SAME_INSTANCE;

                        Logger.LogCpuMonitoringVerboseEvent($"Checking AnalysisRequest LogFileName={analysisRequest.LogFileName}, MachineName={Environment.MachineName}, exceededAnalysisTimelimit={exceededAnalysisTimelimit}, isRequestFileFromSameInstance={isRequestFileFromSameInstance}", analysisRequest.SessionId);

                        if (exceededAnalysisTimelimit || isRequestFileFromSameInstance)
                        {
                            var inprogressFile = Path.ChangeExtension(requestFile, ".inprogress");
                            FileSystemHelpers.MoveFile(requestFile, inprogressFile);
                            AnalyzeKeepingExpirationAlive(inprogressFile);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogCpuMonitoringErrorEvent("Failure in DequeueAnalysisRequest", ex, string.Empty);
                }
            }
        }

        private static void AnalyzeKeepingExpirationAlive(string inprogressFile)
        {
            var maxTimeInMinutes = Infrastructure.Settings.MaxAnalyzerTimeInMinutes;
            var analysisStartTime = DateTime.UtcNow;
            var analysisRequest = FileSystemHelpers.FromJsonFile<AnalysisRequest>(inprogressFile);

            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(maxTimeInMinutes));
                string reportFilePath = AnalyzeDumpFile(analysisRequest, inprogressFile, cts.Token);

                if (!string.IsNullOrWhiteSpace(reportFilePath))
                {
                    UpdateSession(analysisRequest.SessionId, analysisRequest.LogFileName, reportFilePath);
                    FileSystemHelpers.DeleteFileSafe(inprogressFile);
                }
                else
                {
                    HandleErrorsDuringAnalysis(new Exception("DumpAnalyzer did not generate any report files"), analysisRequest, inprogressFile);
                }
            }
            catch (OperationCanceledException ex)
            {
                HandleErrorsDuringAnalysis(ex, analysisRequest, inprogressFile, true);
            }
            catch (Exception ex)
            {
                HandleErrorsDuringAnalysis(ex, analysisRequest, inprogressFile);
            }
        }

        private static void HandleErrorsDuringAnalysis(Exception ex, AnalysisRequest analysisRequest, string inprogressFile, bool cancelFurtherAnalysis = false)
        {
            List<string> errors = new List<string> { ex.Message };

            bool shouldUpdateAnalysisStatus = (analysisRequest.RetryCount >= MAX_ANALYSIS_RETRY_COUNT) || (cancelFurtherAnalysis);
            UpdateSession(analysisRequest.SessionId, analysisRequest.LogFileName, "", errors, shouldUpdateAnalysisStatus);

            // Delete the in-progress file as we have retried too many times or operation was cancelled
            if (shouldUpdateAnalysisStatus)
            {
                FileSystemHelpers.DeleteFileSafe(inprogressFile);
            }
            Logger.LogCpuMonitoringErrorEvent($"HandleErrorsDuringAnalysis, retry count is {analysisRequest.RetryCount}, cancelFurtherAnalysis = {cancelFurtherAnalysis}", ex, analysisRequest.SessionId);
        }

        private static string AnalyzeDumpFile(AnalysisRequest analysisRequest, string inprogressFile, CancellationToken token)
        {
            var blobSasUri = analysisRequest.BlobSasUri;

            string cpuMonitorPath = MonitoringSessionController.GetCpuMonitoringPath(MonitoringSessionDirectories.Logs);
            var diagnosticToolsPath = Infrastructure.Settings.GetDiagnosticToolsPath();
            string outputPath = Path.Combine(cpuMonitorPath, analysisRequest.SessionId);
            string inputFile = CacheFileInTempDirectory(analysisRequest);
            string args = $@"-File ""{diagnosticToolsPath}\DumpAnalyzer.ps1"" ""{inputFile}"" ""{outputPath}""";
            var command = EnvironmentVariables.PowershellExePath;

            Logger.LogCpuMonitoringVerboseEvent($"Powershell started with args [{args}]", analysisRequest.SessionId);
            double secondsWaited = 0;
            var processStartTime = DateTime.UtcNow;
            var toolProcess = Infrastructure.RunProcess(command, args, analysisRequest.SessionId);
            while (!toolProcess.HasExited)
            {
                //
                // Keep updating the Expiration time while we are waiting for the DumpAnalyzer to finish
                //
                analysisRequest.ExpirationTime = DateTime.UtcNow.AddMinutes(ANALYSIS_HEARTBEAT_EXPIRATION_IN_MINUTES);
                analysisRequest.ToJsonFile(inprogressFile);

                Thread.Sleep(10 * 1000);
                secondsWaited = secondsWaited + 10;

                if (secondsWaited > 120)
                {
                    secondsWaited = 0;
                    Logger.LogCpuMonitoringVerboseEvent($"Waiting for Analysis process {command} {args} to finish. Process running for {DateTime.UtcNow.Subtract(processStartTime).TotalSeconds} seconds", analysisRequest.SessionId);
                }

                if (token != CancellationToken.None && token.IsCancellationRequested)
                {
                    Logger.LogCpuMonitoringVerboseEvent($"Kill tool process [{command} {args}] because cancellation is requested", analysisRequest.SessionId);
                    toolProcess.SafeKillProcess();

                    foreach (var dumpAnalyzer in Process.GetProcesses().Where(x => x.ProcessName.Equals("DumpAnalyzer", StringComparison.OrdinalIgnoreCase)))
                    {
                        Logger.LogCpuMonitoringVerboseEvent($"Going to kill [DumpAnalyzer ({dumpAnalyzer.Id})] because cancellation is requested", analysisRequest.SessionId);
                        dumpAnalyzer.SafeKillProcess();
                    }

                    token.ThrowIfCancellationRequested();
                }
            }

            // Delete the file in the temp directory once analysis is done
            FileSystemHelpers.DeleteFileSafe(inputFile);

            if (toolProcess.ExitCode != 0)
            {
                throw new Exception($"Analysis process exited with error code {toolProcess.ExitCode}");
            }

            var reportNamePattern = Path.GetFileNameWithoutExtension(analysisRequest.LogFileName) + "*.mht";
            var reportFilePath = FileSystemHelpers.GetFilesInDirectory(outputPath, reportNamePattern).FirstOrDefault();
            Logger.LogCpuMonitoringVerboseEvent($"DumpAnalyzer completed and reportPath is [{reportFilePath}]", analysisRequest.SessionId);
            return reportFilePath;
        }

        private static string CacheFileInTempDirectory(AnalysisRequest request)
        {
            string outputPath = MonitoringSessionController.TempFilePath;
            FileSystemHelpers.CreateDirectoryIfNotExists(outputPath);

            string dumpFileInTempDirectory = Path.Combine(outputPath, Path.GetFileName(request.LogFileName));
            Logger.LogCpuMonitoringVerboseEvent($"Caching file {request.LogFileName} to {dumpFileInTempDirectory}", request.SessionId);

            if (!FileSystemHelpers.FileExists(dumpFileInTempDirectory))
            {
                Logger.LogCpuMonitoringVerboseEvent($"File {dumpFileInTempDirectory} does not exist. Copying it locally", request.SessionId);
                if (!string.IsNullOrWhiteSpace(request.BlobSasUri))
                {
                    try
                    {
                        string filePath = Path.Combine("Monitoring", "Logs", request.SessionId, Path.GetFileName(request.LogFileName));
                        var blob = BlobController.GetBlobForFile(filePath, request.BlobSasUri);
                        blob.DownloadToFile(dumpFileInTempDirectory, FileMode.Append);
                        Logger.LogCpuMonitoringVerboseEvent($"Copied file from {request.LogFileName} to {dumpFileInTempDirectory} ", request.SessionId);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogCpuMonitoringErrorEvent("Failed while copying file from Blob", ex, request.SessionId);
                    }
                }
                else
                {
                    FileSystemHelpers.CopyFile(request.LogFileName, dumpFileInTempDirectory);
                    Logger.LogCpuMonitoringVerboseEvent($"Copied file from {request.LogFileName} to {dumpFileInTempDirectory} ", request.SessionId);
                }
            }
            return dumpFileInTempDirectory;
        }

        public static void QueueAnalysisRequest(string sessionId, string logFileName, string blobSasUri)
        {
            try
            {
                Logger.LogCpuMonitoringVerboseEvent($"Going to Queue Analysis request for {sessionId} for {logFileName}", sessionId);
                int retryCount = 0;

            retryLabel:
                string analysisFolderPath = GetAnalysisFolderPath(out bool errorEncountered);
                if (errorEncountered && retryCount < 5)
                {
                    Thread.Sleep(60 * 1000);
                    ++retryCount;
                    Logger.LogCpuMonitoringVerboseEvent($"Encountered error while getting analysis folder path. RetryCount is {retryCount}", sessionId);
                    goto retryLabel;
                }

                if (retryCount == 5)
                {
                    throw new Exception("Storage not ready or in-accessible");
                }

                if (!errorEncountered)
                {
                    var request = new AnalysisRequest()
                    {
                        StartTime = DateTime.UtcNow,
                        SessionId = sessionId,
                        LogFileName = logFileName,
                        ExpirationTime = DateTime.MaxValue,
                        BlobSasUri = blobSasUri,
                        RetryCount = 0
                    };

                    var fileName = $"{ sessionId }_{ Path.GetFileNameWithoutExtension(logFileName)}";
                    var requestFile = $"{fileName}.request";
                    requestFile = Path.Combine(analysisFolderPath, requestFile);

                    var existingRequests = FileSystemHelpers.GetFilesInDirectory(analysisFolderPath, $"{fileName}*", false, SearchOption.TopDirectoryOnly);
                    if (existingRequests.Count > 0)
                    {
                        //
                        // There is already and existing analysis request for the same file which is
                        // either in progress or currently submitted so no need to submit a new one
                        //

                        Logger.LogCpuMonitoringVerboseEvent($"An existing request for the same file exists - [{string.Join(",", existingRequests)}]", sessionId);

                    }
                    else
                    {
                        Logger.LogCpuMonitoringVerboseEvent($"Queued {requestFile} for Analysis", sessionId);
                        request.ToJsonFile(requestFile);
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while queuing analysis request", ex, sessionId);
            }

        }

        static void UpdateSession(string sessionId, string logfileName, string reportFilePath, List<string> errors = null, bool shouldUpdateSessionStatus = true)
        {
            try
            {
                var sessionController = new MonitoringSessionController();
                sessionController.AddReportToLog(sessionId, logfileName, reportFilePath, errors, shouldUpdateSessionStatus);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while updating session", ex, sessionId);
            }

        }

        public static void ReSubmitExpiredRequests()
        {
            string analysisFolderPath = GetAnalysisFolderPath(out bool errorEncountered);
            if (!errorEncountered)
            {
                try
                {
                    var inProgressRequests = FileSystemHelpers.GetFilesInDirectory(analysisFolderPath, "*.inprogress", false, SearchOption.TopDirectoryOnly);
                    foreach (var inprogressFile in inProgressRequests)
                    {
                        var analysisRequest = FileSystemHelpers.FromJsonFile<AnalysisRequest>(inprogressFile);
                        if (analysisRequest.ExpirationTime < DateTime.UtcNow)
                        {
                            Logger.LogCpuMonitoringVerboseEvent($"Found an expired analysis request {inprogressFile} that expired {DateTime.UtcNow.Subtract(analysisRequest.ExpirationTime).TotalSeconds} seconds ago", string.Empty);

                            if (analysisRequest.RetryCount < MAX_ANALYSIS_RETRY_COUNT)
                            {
                                try
                                {
                                    ++analysisRequest.RetryCount;
                                    analysisRequest.ExpirationTime = DateTime.MaxValue;
                                    var requestFile = Path.ChangeExtension(inprogressFile, ".request");
                                    FileSystemHelpers.DeleteFileSafe(inprogressFile);
                                    analysisRequest.ToJsonFile(requestFile);
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogCpuMonitoringErrorEvent("Failed while deleting an expired analysis request", ex, string.Empty);
                                }
                            }
                            else
                            {
                                FileSystemHelpers.DeleteFileSafe(inprogressFile);
                                Logger.LogCpuMonitoringVerboseEvent($"Deleting {inprogressFile} because the analysis retry count was reached", string.Empty);
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logger.LogCpuMonitoringErrorEvent("Error in ReSubmitExpiredRequests", ex, string.Empty);
                }
            }
        }
    }
}
