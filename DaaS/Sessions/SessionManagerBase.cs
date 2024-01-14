// -----------------------------------------------------------------------
// <copyright file="SessionManagerBase.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DaaS.Sessions
{
    public class SessionManagerBase
    {
        public const string DaasSiteExtensionPath = "WEBSITE_DAAS_EXTENSIONPATH";
        public bool IncludeSasUri { get; set; }
        public bool InvokedViaAutomation { get; set; }

        public List<DiagnoserDetails> GetDiagnosers()
        {
            List<DiagnoserDetails> retVal = new List<DiagnoserDetails>();
            foreach (Diagnoser diagnoser in Infrastructure.Settings.Diagnosers)
            {
                retVal.Add(new DiagnoserDetails(diagnoser));
            }

            return retVal;
        }

        public bool ShouldAnalyzeOnCurrentInstance(Session activeSession)
        {
            if (activeSession == null)
            {
                return false;
            }

            if (activeSession.Instances != null &&
                activeSession.Instances.Any(x => x.Equals(Infrastructure.GetInstanceId(), StringComparison.OrdinalIgnoreCase)))
            {
                var activeInstance = activeSession.GetCurrentInstance();
                if (activeInstance != null)
                {
                    if (activeInstance.Status == Status.AnalysisQueued)
                    {
                        Logger.LogSessionVerboseEvent("Current instance should analyze", activeSession.SessionId);
                        return true;
                    }
                    else
                    {
                        Logger.LogSessionVerboseEvent($"Current instance should not analyze. Session status is {activeInstance.Status}", activeSession.SessionId);
                    }
                }
            }

            return false;
        }

        public bool CheckIfAnalysisQueuedForCurrentInstance(Session activeSession)
        {
            if (activeSession == null || activeSession.ActiveInstances == null || activeSession.ActiveInstances.Count == 0)
            {
                return false;
            }

            var currentInstance = activeSession.ActiveInstances.FirstOrDefault(x => x.Name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
            if (currentInstance != null
                && (currentInstance.Status == Status.AnalysisQueued || currentInstance.Status == Status.Analyzing))
            {
                return true;
            }

            return false;
        }

        public bool ShouldCollectOnCurrentInstance(Session activeSession)
        {
            if (activeSession == null)
            {
                return false;
            }

            return activeSession.Instances != null &&
                activeSession.Instances.Any(x => x.Equals(Infrastructure.GetInstanceId(), StringComparison.OrdinalIgnoreCase));
        }

        public bool IsSandboxAvailable()
        {
            return Settings.Instance.IsSandBoxAvailable();
        }

        public Task<bool> UpdateStorageAccount(StorageAccount storageAccount)
        {
            throw new NotImplementedException();
        }

        public Task<StorageAccountValidationResult> ValidateStorageAccount()
        {
            throw new NotImplementedException();
        }

        internal void ThrowIfRequiredSettingsMissing()
        {
            var computeMode = Environment.GetEnvironmentVariable("WEBSITE_COMPUTE_MODE");
            if (computeMode != null && !computeMode.Equals("Dedicated", StringComparison.OrdinalIgnoreCase))
            {
                throw new AccessViolationException("DaaS is only supported on websites running in Dedicated SKU");
            }

            var alwaysOnEnabled = Environment.GetEnvironmentVariable("WEBSITE_SCM_ALWAYS_ON_ENABLED");
            if (int.TryParse(alwaysOnEnabled, out int isAlwaysOnEnabled) && isAlwaysOnEnabled == 0)
            {
                string sku = Environment.GetEnvironmentVariable("WEBSITE_SKU");
                if (!string.IsNullOrWhiteSpace(sku) && !sku.ToLower().Contains("elastic"))
                {
                    throw new DiagnosticSessionAbortedException("Cannot submit a diagnostic session because 'Always-On' is disabled. Please enable Always-On in site configuration and re-submit the session");
                }
            }
        }

        internal void ThrowIfLimitsHitViaAutomation(IEnumerable<Session> completedSessions)
        {
            var maxSessionsPerDay = Infrastructure.Settings.MaxSessionsPerDay;
            var completedSessionsLastDay = completedSessions.Where(x => x.StartTime > DateTime.UtcNow.AddDays(-1)).Count();

            if (completedSessionsLastDay >= maxSessionsPerDay)
            {
                throw new AccessViolationException($"The limit of maximum number of DaaS sessions ({maxSessionsPerDay} per day) has been reached. Either disable"
                    + "the autohealing rule, delete existing sessions or increase MaxSessionsPerDay setting in %home%\\data\\daas\\PrivateSettings.json file."
                    + "(Changing settings, requires a restart of the Kudu Site)");
            }

            var sessionThresholdPeriodInMinutes = Infrastructure.Settings.MaxSessionCountThresholdPeriodInMinutes;
            var maxSessionCountInThresholdPeriod = Infrastructure.Settings.MaxSessionCountInThresholdPeriod;

            var sessionsInThresholdPeriod = completedSessions.Where(x => x.StartTime > DateTime.UtcNow.AddMinutes(-1 * sessionThresholdPeriodInMinutes)).Count();
            if (sessionsInThresholdPeriod >= maxSessionCountInThresholdPeriod)
            {
                throw new AccessViolationException($"To avoid impact to application and disk space, a new DaaS session request is rejected as a total of "
                    + $"{maxSessionCountInThresholdPeriod} DaaS sessions were submitted in the last {sessionThresholdPeriodInMinutes} minutes. Either "
                    + $"disable the autohealing rule, delete existing sessions or increase MaxSessionCountInThresholdPeriod,"
                    + $"MaxSessionCountThresholdPeriodInMinutes setting in %home%\\data\\daas\\PrivateSettings.json "
                    + $"file. (Changing settings, requires a restart of the Kudu Site)");
            }
            else
            {
                Logger.LogVerboseEvent($"MaxSessionCountThresholdPeriodInMinutes is {sessionThresholdPeriodInMinutes} and sessionsInThresholdPeriod is {sessionsInThresholdPeriod} so allowing DaaSConsole to submit a new session");
            }
        }

        internal string GetSessionInstances(Session activeSession)
        {
            if (activeSession == null || activeSession.Instances == null || activeSession.Instances.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", activeSession.Instances);
        }

        internal Collector GetCollectorForSession(string toolName)
        {
            var diagnoser = GetDiagnoserForSession(toolName);
            if (diagnoser == null)
            {
                throw new Exception("Diagnostic tool not found");
            }

            if (diagnoser.Collector == null)
            {
                throw new Exception($"Collector not found in {diagnoser.Name}");
            }

            var collector = new Collector(diagnoser);

            return collector;
        }

        internal Diagnoser GetDiagnoserForSession(Session session)
        {
            return GetDiagnoserForSession(session.Tool);
        }

        internal Diagnoser GetDiagnoserForSession(string toolName)
        {
            return Infrastructure.Settings.Diagnosers.FirstOrDefault(x => x.Name == toolName);
        }

        internal void CleanupTempDataForSession(string sessionId)
        {
            DeleteFolderSafe(Path.Combine(DaasDirectory.LogsTempDir, sessionId), sessionId);
            DeleteFolderSafe(Path.Combine(DaasDirectory.ReportsTempDir, sessionId), sessionId);
        }

        internal static void DeleteFolderSafe(string directory, string sessionIdToLog)
        {
            try
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(directory, ignoreErrors: false);
                FileSystemHelpers.DeleteDirectorySafe(directory, ignoreErrors: false);
            }
            catch (Exception ex)
            {
                Logger.LogSessionWarningEvent($"Exception while deleting {directory}", ex, sessionIdToLog);
            }
        }

        internal Analyzer GetAnalyzerForSession(string toolName)
        {
            var diagnoser = GetDiagnoserForSession(toolName);
            if (diagnoser == null)
            {
                throw new Exception("Diagnostic tool not found");
            }

            if (diagnoser.Analyzer == null)
            {
                throw new Exception($"Analyzer not found in {diagnoser.Name}");
            }

            var analyzer = new Analyzer(diagnoser);
            return analyzer;
        }

        internal List<LogFile> GetCurrentInstanceLogs(Session activeSession)
        {
            if (activeSession.ActiveInstances == null)
            {
                return default;
            }

            ActiveInstance currentInstance = activeSession.GetCurrentInstance();
            if (currentInstance == null)
            {
                return default;
            }

            return currentInstance.Logs;
        }

        internal async Task<List<string>> CacheLogsToTempFolderAsync(List<LogFile> collectedLogs, string sessionId, bool requiresStorageAccount,IStorageService storageService )
        {
            var errors = new List<string>();
            foreach (var log in collectedLogs)
            {
                if (File.Exists(log.TempPath))
                {
                    Logger.LogSessionVerboseEvent($"Log [{log.Name}] is already cached at [{log.TempPath}]. Skipping cache operation", sessionId);
                    continue;
                }

                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    sw.Start();
                    if (requiresStorageAccount)
                    {
                        await DownloadFileFromBlobAsync(sessionId, log, storageService);
                    }
                    else
                    {
                        DownloadFileFromFileSystem(sessionId, log);
                    }

                    sw.Stop();
                    Logger.LogSessionVerboseEvent($"File copied to [{log.TempPath}] after {sw.Elapsed.TotalMinutes:0.0} min", sessionId);
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to cache file [{log.Name}] to temp folder. Exception: {ex.GetType()}:{ex.Message}");
                }
            }

            return errors;
        }

        internal static void DownloadFileFromFileSystem(string sessionId, LogFile log)
        {
            try
            {
                string fullFilePath = Path.Combine(Settings.SiteRootDir, log.PartialPath).ConvertForwardSlashesToBackSlashes();
                FileSystemHelpers.CreateDirectoryIfNotExists(Path.GetDirectoryName(log.TempPath));
                Logger.LogSessionVerboseEvent($"Copying file from [{fullFilePath}] to [{log.TempPath}]", sessionId);
                FileSystemHelpers.CopyFile(fullFilePath, log.TempPath);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed caching file to temp folder from FileSystem", ex, sessionId);
                throw ex;
            }
        }

        internal async Task DownloadFileFromBlobAsync(string sessionId, LogFile log, IStorageService storageService)
        {
            try
            {
                Logger.LogSessionVerboseEvent($"Downloading file from Blob storage [{log.PartialPath}] to [{log.TempPath}]", sessionId);
                await storageService.DownloadFileAsync(log.PartialPath, log.TempPath);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed caching file to temp folder", ex, sessionId);
                throw ex;
            }
        }

        internal List<string> GetAnalyzerErrors(Session activeSession)
        {
            if (activeSession.ActiveInstances == null)
            {
                return default;
            }

            ActiveInstance currentInstance = activeSession.GetCurrentInstance();
            if (currentInstance == null)
            {
                return default;
            }

            return currentInstance.AnalyzerErrors;

        }

        internal void UpdateCollectorStatus(Session activeSession)
        {
            if (activeSession.ActiveInstances == null)
            {
                return;
            }

            foreach (var activeInstance in activeSession.ActiveInstances)
            {
                if (activeInstance == null || activeInstance.Name == null)
                {
                    continue;
                }

                string instancePath = Path.Combine(
                    DaasDirectory.LogsDir,
                    activeSession.SessionId,
                    activeInstance.Name);

                if (!Directory.Exists(instancePath))
                {
                    continue;
                }

                var statusFile = Directory.GetFiles(
                    instancePath,
                    "*diagstatus.diaglog",
                    SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(statusFile))
                {
                    activeInstance.CollectorStatusMessages = ReadStatusFile(statusFile);
                }
            }
        }

        internal void UpdateAnalyzerStatus(Session activeSession)
        {
            if (activeSession.ActiveInstances == null)
            {
                return;
            }

            foreach (var activeInstance in activeSession.ActiveInstances)
            {
                foreach (var log in activeInstance.Logs)
                {
                    if (log == null)
                    {
                        continue;
                    }

                    string logReportPath = Path.Combine(
                        DaasDirectory.ReportsDir,
                        activeSession.SessionId,
                        log.StartTime.ToString(Constants.SessionFileNameFormat));

                    if (!Directory.Exists(logReportPath))
                    {
                        continue;
                    }

                    var statusFile = Directory.GetFiles(
                        logReportPath,
                        "*diagstatus.diaglog",
                        SearchOption.TopDirectoryOnly).FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(statusFile))
                    {
                        var statusMessages = ReadStatusFile(statusFile);
                        activeInstance.AnalyzerStatusMessages.AddRange(statusMessages);
                    }
                }
            }
        }

        internal List<Report> SanitizeReports(List<Report> reports)
        {
            if (reports == null)
            {
                return new List<Report>();
            }

            int minRelativePathSegments = Int32.MaxValue;
            var output = new List<Report>();
            foreach (var report in reports)
            {
                int relativePathSegments = report.PartialPath.Split('/').Length;

                if (relativePathSegments == minRelativePathSegments)
                {
                    output.Add(report);
                }
                else if (relativePathSegments < minRelativePathSegments)
                {
                    minRelativePathSegments = relativePathSegments;
                    output.Clear();
                    output.Add(report);
                }
            }

            return output;
        }

        internal void UpdateRelativePathForLogs(Session session)
        {
            if (session.ActiveInstances == null)
            {
                return;
            }

            var diagnoser = GetDiagnoserForSession(session);
            if (diagnoser == null)
            {
                return;
            }

            foreach (var instance in session.ActiveInstances)
            {
                foreach (var log in instance.Logs)
                {
                    if (diagnoser.RequiresStorageAccount)
                    {
                        log.RelativePath = GetPathWithSasUri(log.PartialPath);
                    }
                    else
                    {
                        log.RelativePath = $"{Utility.GetScmHostName()}/api/vfs/{log.PartialPath}";
                    }
                }
            }
        }

        internal static string GetPathWithSasUri(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            string path = string.Empty;
            try
            {
                var blobSasUri = Settings.Instance.BlobSasUri;
                if (string.IsNullOrWhiteSpace(blobSasUri))
                {
                    blobSasUri = Settings.Instance.AccountSasUri;
                }

                var blobUriSections = blobSasUri.Split('?');
                if (blobUriSections.Length >= 2)
                {
                    path = blobUriSections[0] + "/" + relativePath.ConvertBackSlashesToForwardSlashes() + "?" +
                               string.Join("?", blobUriSections, 1, blobUriSections.Length - 1);
                }
                else
                {
                    path = blobUriSections[0] + "/" + relativePath.ConvertBackSlashesToForwardSlashes();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while getting RelativePath with SAS URI", ex);
            }

            return path;
        }

        internal async Task DeleteSessionContentsAsync(Session session, IStorageService storageService)
        {
            bool deleteFilesFromBlob = false;
            var diagnoser = GetDiagnoserForSession(session);
            if (diagnoser.RequiresStorageAccount)
            {
                deleteFilesFromBlob = true;
            }

            if (deleteFilesFromBlob)
            {
                await DeleteLogsFromBlobAsync(session, storageService);
            }

            DeleteFolderSafe(Path.Combine(DaasDirectory.LogsDir, session.SessionId), session.SessionId);
            DeleteFolderSafe(Path.Combine(DaasDirectory.ReportsDir, session.SessionId), session.SessionId);
        }

        internal void LogSessionDetailsSafe(Session session, bool isV2Session)
        {
            try
            {
                var details = new
                {
                    Instances = string.Join(",", session.Instances),
                    HostName = session.DefaultScmHostName,
                    session.BlobStorageHostName,
                    Sku = Environment.GetEnvironmentVariable("WEBSITE_SKU"),
                    invokedViaDaasConsole = InvokedViaAutomation,
                    session.Description,
                    ConnectionStringConfigured = !string.IsNullOrWhiteSpace(Settings.Instance.StorageConnectionString),
                    SasUriConfigured = !string.IsNullOrWhiteSpace(Settings.Instance.AccountSasUri),
                    isV2Session
                };

                Logger.LogNewSession(
                    session.SessionId,
                    session.Mode.ToString(),
                    session.Tool,
                    details);
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed while logging session details", ex);
            }
        }

        private async Task DeleteLogsFromBlobAsync(Session session, IStorageService storageService)
        {
            if (session.ActiveInstances == null)
            {
                return;
            }

            foreach (var activeInstance in session.ActiveInstances)
            {
                foreach (var log in activeInstance.Logs)
                {
                    await DeleteLogFromBlobAsync(log, session.SessionId, storageService);
                }
            }
        }

        private async Task DeleteLogFromBlobAsync(LogFile log, string sessionId, IStorageService storageService)
        {
            try
            {
                await storageService.DeleteFileAsync(log.PartialPath);

            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent($"Failed while deleting {log.PartialPath} from blob", ex, sessionId);
            }
        }

        private static List<string> ReadStatusFile(string statusFile)
        {
            var messages = new List<string>();
            try
            {
                using (FileStream fs = File.Open(statusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    while (!sr.EndOfStream)
                    {
                        messages.Add(sr.ReadLine());
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent($"Failed to read status file", ex);
            }

            return messages;
        }
    }
}
