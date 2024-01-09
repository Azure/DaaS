// -----------------------------------------------------------------------
// <copyright file="SessionManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.Storage;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS.Sessions
{

    public class SessionManager : ISessionManager
    {
        public const string DaasDiagLauncherEnv = "WEBSITE_DAAS_DIAG_LAUNCHER";

        private static readonly AlertingStorageQueue _alertingStorageQueue = new AlertingStorageQueue();
        private static IOperationLock _sessionLockFile;
        
        private readonly IStorageService _storageService;

        private readonly List<string> _allSessionsDirs = new List<string>()
        {
            SessionDirectories.ActiveSessionsDir,
            SessionDirectories.CompletedSessionsDir,
            SessionDirectories.ActiveSessionsV2Dir,
            SessionDirectories.CompletedSessionsV2Dir
        };

        public SessionManager(IStorageService storageService)
        {
            _storageService = storageService;
            EnsureSessionDirectories();
        }

        #region ISessionManager methods

        public async Task<Session> GetActiveSessionAsync(bool isV2Session, bool isDetailed = false)
        {
            var activeSessions = await LoadSessionsAsync(isV2Session ? SessionDirectories.ActiveSessionsV2Dir : SessionDirectories.ActiveSessionsDir, isDetailed);
            var activeSession = activeSessions.FirstOrDefault();
            return activeSession;
        }

        public async Task CheckIfOrphaningOrTimeoutNeededAsync(Session activeSession)
        {
            if (activeSession == null)
            {
                return;
            }

           if (CheckIfTimeLimitExceeded(activeSession))
            {
                Logger.LogSessionErrorEvent("Allowed time limit exceeded for the session", $"Session was started at {activeSession.StartTime}", activeSession.SessionId);
                await MarkSessionAsCompleteAsync(activeSession, isV2Session: true, forceCompletion: true);
            }
            else
            {
                if (DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes > Settings.Instance.OrphanInstanceTimeoutInMinutes)
                {
                    await CancelOrphanedInstancesIfNeeded(isV2Session: true);
                    await CheckandCompleteSessionIfNeededAsync(isV2Session: true);
                }
            }
        }

        public async Task<IEnumerable<Session>> GetAllSessionsAsync(bool isDetailed = false)
        {
            return (await LoadSessionsAsync(_allSessionsDirs, isDetailed)).OrderByDescending(x => x.StartTime);
        }

        public async Task<IEnumerable<Session>> GetCompletedSessionsAsync(bool isV2Session)
        {
            return (await LoadSessionsAsync(isV2Session ? SessionDirectories.CompletedSessionsV2Dir : SessionDirectories.CompletedSessionsDir)).OrderByDescending(x => x.StartTime);
        }

        public async Task<Session> GetSessionAsync(string sessionId, bool isDetailed = false)
        {
            return (await LoadSessionsAsync(_allSessionsDirs, isDetailed))
                .Where(x => x.SessionId == sessionId).FirstOrDefault();
        }

        public async Task<string> SubmitNewSessionAsync(Session session, bool isV2Session, bool invokedViaDaasConsole = false)
        {
            string sessionId = string.Empty;
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

            var activeSession = await GetActiveSessionAsync(isV2Session);
            if (activeSession != null)
            {
                if (isV2Session && IsSameSession(session, activeSession))
                {
                    //
                    // For V2 sessions, multiple instances will try to submit the same session.
                    // If we find an existing active session with the same session id, we will
                    // assume that it is the same session
                    //

                    return activeSession.SessionId;
                }

                throw new AccessViolationException($"There is already an existing session for {activeSession.Tool}");
            }

            await ThrowIfSessionInvalid(session, isV2Session);

            //
            // Acquire lock on the Active Session file
            //

            var activeSessionLock = await AcquireActiveSessionLockAsync(isV2Session);
            if (activeSessionLock == null)
            {
                //
                // If we failed to acquire the lock, someone else might have already created a session.
                // Throw an exception to indicate that there is an active session
                //

                throw new AccessViolationException("Failed to acquire the lock to create a session. There is most likely another active session");
            }

            string existingSessionMessage = string.Empty;
            try
            {
                var existingActiveSession = await GetActiveSessionAsync(isV2Session);
                if (existingActiveSession == null)
                {
                    //
                    // Save session only if there is no existing active session
                    //

                    sessionId = await SaveSessionAsync(session, isV2Session, invokedViaDaasConsole);
                }
                else
                {
                    if (isV2Session && IsSameSession(session, existingActiveSession))
                    {
                        //
                        // For V2 sessions, multiple instances will try to submit the same session.
                        // If we find an existing active session with the same session id, we will
                        // assume that it is the same session. Also do not return here, make sure
                        // that the active session lock gets released.
                        //

                        sessionId = existingActiveSession.SessionId;
                    }
                    else
                    {
                        existingSessionMessage = $"Existing session '{existingActiveSession.SessionId}' for '{existingActiveSession.Tool}' found";
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed while submitting session", ex, "NEW_SESSION_ID");
            }
            finally
            {
                activeSessionLock.Release();
            }

            if (!string.IsNullOrWhiteSpace(existingSessionMessage))
            {
                throw new AccessViolationException(existingSessionMessage);
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new Exception("Failed to create a new session");
            }

            return sessionId;
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

        private bool CheckIfTimeLimitExceeded(Session activeSession)
        {
            var duration = DateTime.UtcNow.Subtract(activeSession.StartTime);
            if (duration.TotalMinutes > Settings.Instance.MaxSessionTimeInMinutes)
            {
                return true;
            }

            return false;
        }

        private bool IsSameSession(Session session, Session activeSession)
        {
            return !string.IsNullOrWhiteSpace(session.SessionId)
                && activeSession.SessionId.Equals(session.SessionId, StringComparison.OrdinalIgnoreCase)
                && session.Tool == activeSession.Tool;
        }

        private async Task ThrowIfSessionInvalid(Session session, bool isV2Session)
        {
            if (session.Instances == null || !session.Instances.Any())
            {
                throw new ArgumentException("At least one instance must be specified");
            }

            if (string.IsNullOrWhiteSpace(session.Tool))
            {
                throw new ArgumentException("Please specify a valid diagnostic tool to run");
            }

            var diagnoser = GetDiagnoserForSession(session);
            if (diagnoser == null)
            {
                throw new ArgumentException($"Invalid diagnostic tool '{session.Tool}' ");
            }

            ThrowIfInvalidStorageConfiguration(diagnoser);

            if (InvokedViaAutomation)
            {
                var maxSessionsPerDay = Infrastructure.Settings.MaxSessionsPerDay;

                var completedSessions = await GetCompletedSessionsAsync(isV2Session);
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
        }

        private void ThrowIfInvalidStorageConfiguration(Diagnoser diagnoser)
        {
            if (!diagnoser.RequiresStorageAccount)
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(Settings.Instance.StorageConnectionString) && string.IsNullOrWhiteSpace(Settings.Instance.AccountSasUri))
                {
                    throw new ArgumentException($"The tool '{diagnoser.Name}' requires that WEBSITE_DAAS_STORAGE_CONNECTIONSTRING setting must be specified");
                }

                if (!_storageService.ValidateStorageConfiguration(out string storageAccount, out Exception exceptionContactingStorage))
                {
                    if (exceptionContactingStorage != null)
                    {
                        throw new DiagnosticSessionAbortedException($"Storage configuration is invalid - {exceptionContactingStorage.Message}", exceptionContactingStorage);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new DiagnosticSessionAbortedException($"Storage configuration is invalid - {ex.Message}", ex);
            }
        }

        public async Task<bool> HasThisInstanceCollectedLogs(bool isV2Session)
        {
            var activeSession = await GetActiveSessionAsync(isV2Session);
            return activeSession.ActiveInstances != null
                && activeSession.ActiveInstances.Any(x => x.Name.Equals(Infrastructure.GetInstanceId(),
                StringComparison.OrdinalIgnoreCase) && x.Status == Status.Complete);
        }

        public async Task RunToolForSessionAsync(Session activeSession, bool isV2Session, bool queueAnalysisRequest, CancellationToken token)
        {
            try
            {
                string sessionId = activeSession.SessionId;
                var activeInstance = activeSession.GetCurrentInstance();

                //
                // It's possible that DaasRunner restarts after the data is already collected. In that
                // situation, the current Instance status would be set to Analyzing, TimedOut or Complete
                // Just make sure that we are not past that stage for the current session.
                //

                if (activeInstance == null || activeInstance.Status == Status.Active || activeInstance.Status == Status.Started)
                {
                    DiagnosticToolResponse resp = null;
                    Collector collector = GetCollectorForSession(activeSession.Tool);
                    await SetCurrentInstanceAsStartedAsync(activeSession, isV2Session);

                    try
                    {
                        resp = await collector.CollectLogsAsync(
                            activeSession,
                            _storageService,
                            token);
                    }
                    catch (Exception ex)
                    {
                        resp = new DiagnosticToolResponse();
                        resp.Errors.Add($"Invoking diagnostic tool failed with error - {ex.Message}");
                        Logger.LogSessionErrorEvent("Tool invocation failed", ex, activeSession.SessionId);
                    }

                    //
                    // Add the tool output to the active session
                    //
                    await AppendCollectorResponseToSessionAsync(activeSession, isV2Session, resp);
                }

                if (isV2Session && queueAnalysisRequest)
                {
                    await SetCurrentInstanceAsAnalysisQueuedAsync(activeSession, isV2Session);
                    return;
                }

                await AnalyzeAndCompleteSessionAsync(activeSession, isV2Session, sessionId, token);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Exception while running tool", ex, activeSession.SessionId);
            }
        }

        public async Task AnalyzeAndCompleteSessionAsync(Session activeSession, bool isV2Session, string sessionId, CancellationToken token)
        {
            //
            // Mark current instance as Analyzing
            //
            await SetCurrentInstanceAsAnalyzingAsync(activeSession, isV2Session);

            //
            // Fire Analysis task
            //
            await AnalyzeAndUpdateSessionAsync(token, sessionId, isV2Session);

            //
            // Mark current instance as Complete
            //
            await SetCurrentInstanceAsCompleteAsync(activeSession, isV2Session);

            //
            // Cleanup all the temporary collected data
            //
            CleanupTempDataForSession(activeSession.SessionId);

            //
            // Check if all the instances have finished running the session
            // and set the Session State to Complete
            //
            await CheckandCompleteSessionIfNeededAsync(isV2Session);
        }

        private void CleanupTempDataForSession(string sessionId)
        {
            DeleteFolderSafe(Path.Combine(DaasDirectory.LogsTempDir, sessionId), sessionId);
            DeleteFolderSafe(Path.Combine(DaasDirectory.ReportsTempDir, sessionId), sessionId);
        }

        public async Task<bool> CheckandCompleteSessionIfNeededAsync(bool isV2Session, bool forceCompletion = false)
        {
            var activeSession = await GetActiveSessionAsync(isV2Session);
            if (activeSession == null)
            {
                return true;
            }

            if (AllInstancesFinished(activeSession) || forceCompletion)
            {
                Logger.LogSessionVerboseEvent("All instances have status as Complete", activeSession.SessionId);
                await MarkSessionAsCompleteAsync(activeSession, isV2Session, forceCompletion: forceCompletion);
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
                if (activeInstance != null )
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

        public bool IsSessionExisting(string sessionId, bool isV2Session)
        {
            var sessionFile = Path.Combine(isV2Session ? SessionDirectories.CompletedSessionsV2Dir : SessionDirectories.CompletedSessionsDir, sessionId + ".json");
            return FileSystemHelpers.FileExists(sessionFile);
        }

        public async Task DeleteSessionAsync(string sessionId, bool isV2Session)
        {
            EnsureSessionDirectories();

            await Task.Run(async () =>
            {
                var sessionFile = Path.Combine(isV2Session ? SessionDirectories.CompletedSessionsV2Dir : SessionDirectories.CompletedSessionsDir, sessionId + ".json");
                if (!FileSystemHelpers.FileExists(sessionFile))
                {
                    throw new ArgumentException($"Session {sessionId} does not exist");
                }

                try
                {
                    var deleteFilesFromBlob = false;
                    var session = await FromJsonFileAsync<Session>(sessionFile); ;
                    var diagnoser = GetDiagnoserForSession(session);
                    if (diagnoser.RequiresStorageAccount)
                    {
                        deleteFilesFromBlob = true;
                    }

                    if (deleteFilesFromBlob)
                    {
                        await DeleteLogsFromBlob(session);
                    }

                    DeleteFolderSafe(Path.Combine(DaasDirectory.LogsDir, sessionId), sessionId);
                    DeleteFolderSafe(Path.Combine(DaasDirectory.ReportsDir, sessionId), sessionId);
                    FileSystemHelpers.DeleteFile(sessionFile);
                    Logger.LogSessionVerboseEvent("Session deleted", sessionId);
                }
                catch (Exception ex)
                {
                    Logger.LogSessionWarningEvent("Failed while deleting session", ex, sessionId);
                    throw;
                }
            });
        }

        private static void DeleteFolderSafe(string directory, string sessionIdToLog)
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

        private async Task DeleteLogsFromBlob(Session session)
        {
            if (session.ActiveInstances == null)
            {
                return;
            }

            foreach (var activeInstance in session.ActiveInstances)
            {
                foreach (var log in activeInstance.Logs)
                {
                    await DeleteLogFromBlob(log, session.SessionId);
                }
            }
        }

        private async Task DeleteLogFromBlob(LogFile log, string sessionId)
        {
            try
            {
                await _storageService.DeleteFileAsync(log.PartialPath);
                
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent($"Failed while deleting {log.PartialPath} from blob", ex, sessionId);
            }
        }

        public List<DiagnoserDetails> GetDiagnosers()
        {
            List<DiagnoserDetails> retVal = new List<DiagnoserDetails>();
            foreach (Diagnoser diagnoser in Infrastructure.Settings.Diagnosers)
            {
                retVal.Add(new DiagnoserDetails(diagnoser));
            }

            return retVal;
        }

        public bool IsSandboxAvailable()
        {
            return Settings.Instance.IsSandBoxAvailable();
        }

        public async Task CancelOrphanedInstancesIfNeeded(bool isV2Session)
        {
            var activeSession = await GetActiveSessionAsync(isV2Session);
            if (activeSession == null)
            {
                return;
            }

            // If none of the instances picked up the session

            var orphanedInstanceNames = new List<string>();
            if (activeSession.ActiveInstances == null || activeSession.ActiveInstances.Count == 0)
            {
                Logger.LogSessionVerboseEvent("activeSession.ActiveInstances is NULL or count is 0", activeSession.SessionId);
                orphanedInstanceNames = activeSession.Instances;
            }
            else
            {
                var activeInstances = activeSession.ActiveInstances.Select(x => x.Name);
                orphanedInstanceNames = activeSession.Instances.Where(x => !activeInstances.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
                if (orphanedInstanceNames != null)
                {
                    Logger.LogSessionVerboseEvent($"ActiveSessionJson = {JsonConvert.SerializeObject(activeSession)}", activeSession.SessionId);
                    Logger.LogSessionVerboseEvent($"orphanedInstanceNames = {string.Join(",", orphanedInstanceNames)}", activeSession.SessionId);
                }
            }

            if (orphanedInstanceNames == null || !orphanedInstanceNames.Any())
            {
                Logger.LogSessionVerboseEvent($"Returning as we found no orphaned instances", activeSession.SessionId);
                return;
            }

            Logger.LogSessionErrorEvent("Identified orphaned instances for session",
                $"Orphaning instance(s) {string.Join(",", orphanedInstanceNames)} as they haven't picked up the session",
                activeSession.SessionId);

            var orphanedInstances = new List<ActiveInstance>();
            foreach (var instance in orphanedInstanceNames)
            {
                var activeInstance = new ActiveInstance(instance)
                {
                    Status = Status.Complete
                };

                activeInstance.CollectorErrors.Add($"The instance [{instance}] did not pick up the session within the required time");
                orphanedInstances.Add(activeInstance);
            }

            await UpdateActiveSessionAsync((latestSessionFromDisk) =>
            {
                try
                {
                    if (latestSessionFromDisk.ActiveInstances == null)
                    {
                        latestSessionFromDisk.ActiveInstances = orphanedInstances;
                    }
                    else
                    {
                        foreach (var orphanedInstance in orphanedInstances)
                        {
                            if (!latestSessionFromDisk.ActiveInstances.Any(x => x.Name == orphanedInstance.Name))
                            {
                                latestSessionFromDisk.ActiveInstances.Add(orphanedInstance);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogSessionErrorEvent("Failed while updating orphaned instances for the session", ex, latestSessionFromDisk.SessionId);
                }

                return latestSessionFromDisk;
            }, activeSession.SessionId, isV2Session: isV2Session, callerMethodName: "CancelOrphanedInstancesIfNeeded");
        }

        public bool IncludeSasUri { get; set; }
        public bool InvokedViaAutomation { get; set; }
        #endregion

        private Diagnoser GetDiagnoserForSession(Session session)
        {
            return GetDiagnoserForSession(session.Tool);
        }

        private Diagnoser GetDiagnoserForSession(string toolName)
        {
            return Infrastructure.Settings.Diagnosers.FirstOrDefault(x => x.Name == toolName);
        }

        private async Task AnalyzeAndUpdateSessionAsync(CancellationToken token, string sessionId, bool isV2Session)
        {
            var activeSession = await GetActiveSessionAsync(isV2Session);
            if (activeSession == null)
            {
                Logger.LogSessionWarningEvent("Failed while analyzing the session", "ActiveSession is NULL. Another instance might have completed the session", sessionId);
                return;
            }

            if (activeSession.Mode == Mode.Collect)
            {
                return;
            }

            Logger.LogSessionVerboseEvent("Getting collected logs for session", sessionId);
            var collectedLogs = GetCurrentInstanceLogs(activeSession);
            var analyzerErrors = new List<string>();
            if (collectedLogs.Count > 0)
            {
                Logger.LogSessionVerboseEvent($"Identified {collectedLogs.Count} logs to analyze", sessionId);
                var analyzer = GetAnalyzerForSession(activeSession.Tool);

                if (isV2Session)
                {
                    var errors = await CacheLogsToTempFolderAsync(collectedLogs, sessionId, analyzer.RequiresStorageAccount);
                    if (errors.Any())
                    {
                        analyzerErrors = analyzerErrors.Union(errors).ToList();
                    }
                    else
                    {
                        Logger.LogSessionVerboseEvent("Issuing analysis for session", sessionId);
                        await analyzer.AnalyzeLogsAsync(collectedLogs, activeSession, token);
                    }
                }
                else
                {
                    Logger.LogSessionVerboseEvent("Issuing analysis for session", sessionId);
                    await analyzer.AnalyzeLogsAsync(collectedLogs, activeSession, token);
                }
            }

            analyzerErrors = analyzerErrors.Union(GetAnalyzerErrors(activeSession)).ToList();
            Logger.LogSessionVerboseEvent($"Analysis completed. Analysis errors = {string.Join(",", analyzerErrors)}", sessionId);

            await UpdateActiveSessionAsync((latestSessionFromDisk) =>
            {
                try
                {
                    ActiveInstance activeInstance = latestSessionFromDisk.GetCurrentInstance();
                    if (activeInstance == null)
                    {
                        return latestSessionFromDisk;
                    }

                    foreach (var log in collectedLogs)
                    {
                        var existingLog = activeInstance.Logs.FirstOrDefault(x => x.Name.Equals(log.Name, StringComparison.OrdinalIgnoreCase));
                        if (existingLog == null)
                        {
                            return latestSessionFromDisk;
                        }
                        existingLog.Reports.AddRange(log.Reports);
                    }

                    //
                    // Append any errors from Analyzer
                    //

                    activeInstance.AnalyzerErrors = activeInstance.AnalyzerErrors.Union(analyzerErrors).Distinct().ToList();
                }
                catch (Exception ex)
                {
                    Logger.LogSessionErrorEvent("Failed while updating reports for the session", ex, latestSessionFromDisk.SessionId);
                }

                return latestSessionFromDisk;
            }, activeSession.SessionId, isV2Session: isV2Session, callerMethodName: "AnalyzeAndUpdateSessionAsync");
        }

        private async Task<List<string>> CacheLogsToTempFolderAsync(List<LogFile> collectedLogs, string sessionId, bool requiresStorageAccount)
        {
            var errors = new List<string>();
            foreach(var log in collectedLogs)
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
                        await DownloadFileFromBlobAsync(sessionId, log);
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

        private static void DownloadFileFromFileSystem(string sessionId, LogFile log)
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

        private async Task DownloadFileFromBlobAsync(string sessionId, LogFile log)
        {
            try
            {
                Logger.LogSessionVerboseEvent($"Downloading file from Blob storage [{log.PartialPath}] to [{log.TempPath}]", sessionId);
                await _storageService.DownloadFileAsync(log.PartialPath, log.TempPath);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed caching file to temp folder", ex, sessionId);
                throw ex;
            }
        }

        private List<string> GetAnalyzerErrors(Session activeSession)
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

        private List<LogFile> GetCurrentInstanceLogs(Session activeSession)
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

        private Analyzer GetAnalyzerForSession(string toolName)
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

        private Collector GetCollectorForSession(string toolName)
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

        private void UpdateAnalyzerStatus(Session activeSession)
        {
            foreach (var activeInstance in activeSession.ActiveInstances)
            {
                foreach (var log in activeInstance.Logs)
                {
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

        private void UpdateCollectorStatus(Session activeSession)
        {
            foreach (var activeInstance in activeSession.ActiveInstances)
            {
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

        private async Task AppendCollectorResponseToSessionAsync(Session activeSession, bool isV2Session, DiagnosticToolResponse response)
        {
            try
            {
                await UpdateActiveSessionAsync((latestSessionFromDisk) =>
                {
                    try
                    {
                        if (latestSessionFromDisk.ActiveInstances == null)
                        {
                            latestSessionFromDisk.ActiveInstances = new List<ActiveInstance>();
                        }

                        ActiveInstance activeInstance = latestSessionFromDisk.GetCurrentInstance();
                        if (activeInstance == null)
                        {
                            activeInstance = new ActiveInstance(Infrastructure.GetInstanceId());
                            latestSessionFromDisk.ActiveInstances.Add(activeInstance);
                            Logger.LogSessionVerboseEvent($"Added ActiveInstance to session", latestSessionFromDisk.SessionId);
                        }

                        if (response.Logs != null)
                        {
                            foreach (var log in response.Logs)
                            {
                                if (activeInstance.Logs.Any(x => x.Name == log.Name && x.Size == log.Size))
                                {
                                    continue;
                                }

                                activeInstance.Logs.Add(log);
                            }
                        }

                        if (response.Errors != null && response.Errors.Any())
                        {
                            activeInstance.CollectorErrors = activeInstance.CollectorErrors.Union(response.Errors).Distinct().ToList();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogSessionErrorEvent("Failed while adding active instance", ex, latestSessionFromDisk.SessionId);
                    }

                    return latestSessionFromDisk;
                }, activeSession.SessionId, isV2Session, callerMethodName: "AppendCollectorResponseToSessionAsync");
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed in AddLogsToActiveSession", ex, activeSession.SessionId);
            }
        }

        private async Task UpdateActiveSessionAsync(Func<Session, Session> updateSession, string sessionId, bool isV2Session, string callerMethodName)
        {
            try
            {
                _sessionLockFile = await AcquireSessionLockAsync(sessionId,isV2Session, callerMethodName);

                if (_sessionLockFile == null)
                {
                    //
                    // We failed to acquire the lock on the session file
                    //

                    return;
                }

                //
                // Load the latest session from the disk under the lock. This
                // ensures that only one instance can write to the sesion
                // and others wait while this instance has the lock
                //

                Session latestSessionFromDisk = await GetActiveSessionAsync(isV2Session);
                if (latestSessionFromDisk == null)
                {
                    return;
                }

                Session sessionAfterMergingLatestUpdates = updateSession(latestSessionFromDisk);
                if (sessionAfterMergingLatestUpdates == null)
                {
                    return;
                }

                await UpdateActiveSessionFileAsync(sessionAfterMergingLatestUpdates, isV2Session);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent($"Failed while updating session", ex, sessionId);
            }

            if (_sessionLockFile != null)
            {
                Logger.LogSessionVerboseEvent($"SessionLock released by {callerMethodName}", sessionId);
                _sessionLockFile.Release();
            }
        }

        private async Task<List<Session>> LoadSessionsAsync(string directoryToLoadSessionsFrom, bool isDetailed = false)
        {
            return await LoadSessionsAsync(new List<string> { directoryToLoadSessionsFrom }, isDetailed);
        }

        private async Task<List<Session>> LoadSessionsAsync(List<string> directoriesToLoadSessionsFrom, bool isDetailed = false)
        {
            EnsureSessionDirectories();
            var sessions = new List<Session>();

            try
            {
                foreach (var directory in directoriesToLoadSessionsFrom)
                {
                    foreach (var sessionFile in FileSystemHelpers.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        await LoadSingleSessionAsync(isDetailed, sessions, sessionFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent($"Failed while loading sessions", ex);
            }

            return sessions;
        }

        private async Task LoadSingleSessionAsync(bool isDetailed, List<Session> sessions, string sessionFile)
        {
            try
            {
                var session = await FromJsonFileAsync<Session>(sessionFile);
                if (IncludeSasUri)
                {
                    UpdateRelativePathForLogs(session);
                }

                //
                // For Active session, populate Collector and Analyzer detailed status
                //

                if (isDetailed && session.ActiveInstances != null)
                {
                    if (session.Status == Status.Active)
                    {
                        UpdateCollectorStatus(session);
                        UpdateAnalyzerStatus(session);
                    }

                    foreach (var activeInstance in session.ActiveInstances)
                    {
                        foreach (var log in activeInstance.Logs)
                        {
                            log.Reports = SanitizeReports(log.Reports);
                        }
                    }
                }

                sessions.Add(session);
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent($"Failed while loading session - {Path.GetFileName(sessionFile)}", ex);
            }
        }

        private List<Report> SanitizeReports(List<Report> reports)
        {
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

        private void UpdateRelativePathForLogs(Session session)
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

        private static string GetPathWithSasUri(string relativePath)
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

        private async Task<string> SaveSessionAsync(Session session, bool isV2Session, bool invokedViaDaasConsole)
        {
            try
            {
                session.StartTime = DateTime.UtcNow;
                if (isV2Session && !string.IsNullOrWhiteSpace(session.SessionId))
                {
                    //
                    // For V2 sessions, session id is generated by the client
                    //
                }
                else
                {
                    session.SessionId = GetSessionId(session.StartTime);
                }

                session.Status = Status.Active;

                var diagnoser = GetDiagnoserForSession(session);
                if (diagnoser != null && diagnoser.RequiresStorageAccount)
                {
                    session.BlobStorageHostName = _storageService.GetBlobStorageHostName();
                }

                session.DefaultScmHostName = Settings.Instance.DefaultScmHostName;
                await WriteJsonAsync(session,
                    Path.Combine(isV2Session ? SessionDirectories.ActiveSessionsV2Dir : SessionDirectories.ActiveSessionsDir, session.SessionId + ".json"));

                LogSessionDetailsSafe(session, invokedViaDaasConsole, isV2Session);
                return session.SessionId;
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed while saving the session", ex, session.SessionId);
            }

            return string.Empty;
        }

        private static void LogSessionDetailsSafe(Session session, bool invokedViaDaasConsole, bool isV2Session)
        {
            try
            {
                var details = new
                {
                    Instances = string.Join(",", session.Instances),
                    HostName = session.DefaultScmHostName,
                    session.BlobStorageHostName,
                    Sku = Environment.GetEnvironmentVariable("WEBSITE_SKU"),
                    invokedViaDaasConsole,
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

        private string GetSessionId(DateTime startTime)
        {
            return startTime.ToString(Constants.SessionFileNameFormat);
        }

        private async Task WriteJsonAsync(object objectToSerialize, string filePath)
        {
            await WriteTextAsync(filePath, JsonConvert.SerializeObject(objectToSerialize, Formatting.Indented));
        }

        private async Task<T> FromJsonFileAsync<T>(string filePath)
        {
            string fileContents = await ReadTextAsync(filePath);
            T obj = JsonConvert.DeserializeObject<T>(fileContents);
            return obj;
        }

        async Task<string> ReadTextAsync(string path)
        {
            var sb = new StringBuilder();
            using (var sourceStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 4096, useAsync: true))
            {
                byte[] buffer = new byte[0x1000];
                int numRead;
                while ((numRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    string text = Encoding.UTF8.GetString(buffer, 0, numRead);
                    sb.Append(text);
                }

                return sb.ToString();
            }
        }

        async Task WriteTextAsync(string filePath, string text)
        {
            byte[] encodedText = Encoding.UTF8.GetBytes(text);

            using (var sourceStream =
                new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096,
                useAsync: true))
            {
                await sourceStream.WriteAsync(encodedText, 0, encodedText.Length);
            }
        }

        private async Task<IOperationLock> AcquireSessionLockAsync(string sessionId, bool isV2Session, string callerMethodName)
        {
            IOperationLock sessionLock = new SessionLockFile(GetActiveSessionLockPath(sessionId, isV2Session));
            int loopCount = 0;

            Logger.LogSessionVerboseEvent($"Acquiring SessionLock by {callerMethodName}", sessionId);
            while (!sessionLock.Lock(callerMethodName)
                && loopCount <= 60)
            {
                ++loopCount;
                await Task.Delay(1000);
            }

            if (loopCount > 60)
            {
                Logger.LogSessionVerboseEvent($"Deleting the lock file as it seems to be in an orphaned stage", sessionId);
                sessionLock.Release();
                return null;
            }

            Logger.LogSessionVerboseEvent($"Acquired SessionLock by {callerMethodName}", sessionId);
            return sessionLock;
        }

        private async Task<IOperationLock> AcquireActiveSessionLockAsync(bool isV2Session)
        {
            string lockFilePath = Path.Combine(isV2Session ? SessionDirectories.ActiveSessionsV2Dir: SessionDirectories.ActiveSessionsDir, "activesession.json.lock");
            IOperationLock sessionLock = new SessionLockFile(lockFilePath);
            if (isV2Session)
            {
                //
                // For V2 sessions, multiple instances will attempt to Create the session at the same time.
                // Introduce some delay to ensure that we allow the Submit session call to succeed
                //

                int loopCount = 0;
                Logger.LogSessionVerboseEvent($"Acquiring ActiveSessionLock by AcquireActiveSessionLock", "NEW_SESSION_ID");
                while (!sessionLock.Lock("AcquireActiveSessionLock") && loopCount <= 10)
                {
                    ++loopCount;
                    Logger.LogSessionVerboseEvent($"Waiting to aquire ActiveSessionLock", "NEW_SESSION_ID");
                    await Task.Delay(1000);
                }

                if (loopCount > 10)
                {
                    Logger.LogSessionVerboseEvent($"Deleting the lock file as it seems to be in an orphaned stage", "NEW_SESSION_ID");
                    sessionLock.Release();
                    return null;
                }

                Logger.LogSessionVerboseEvent($"Acquiring ActiveSessionLock by AcquireActiveSessionLock", "NEW_SESSION_ID");
                return sessionLock;
            }
            else
            {
                Logger.LogSessionVerboseEvent($"Acquiring ActiveSessionLock by AcquireActiveSessionLock", "NEW_SESSION_ID");
                if (sessionLock.Lock("AcquireActiveSessionLock"))
                {
                    Logger.LogSessionVerboseEvent($"Acquired ActiveSessionLock by AcquireActiveSessionLock", "NEW_SESSION_ID");
                    return sessionLock;
                }
            }

            return null;
        }

        private async Task UpdateActiveSessionFileAsync(Session activeSesion, bool isV2Session)
        {
          await WriteJsonAsync(
                activeSesion,
                Path.Combine(
                    isV2Session ? SessionDirectories.ActiveSessionsV2Dir : SessionDirectories.ActiveSessionsDir,
                    activeSesion.SessionId + ".json"));
        }

        private string GetActiveSessionLockPath(string sessionId, bool isV2Session)
        {
            return Path.Combine(
                isV2Session ? SessionDirectories.ActiveSessionsV2Dir : SessionDirectories.ActiveSessionsDir,
                sessionId + ".json.lock");
        }

        private async Task SetCurrentInstanceAsCompleteAsync(Session activeSession, bool isV2Session)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as Complete", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, isV2Session, Status.Complete);
        }

        private async Task SetCurrentInstanceAsStartedAsync(Session activeSession, bool isV2Session)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as Started", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, isV2Session, Status.Started);
        }

        private async Task SetCurrentInstanceAsAnalysisQueuedAsync(Session activeSession, bool isV2Session)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as AnalysisQueued", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, isV2Session, Status.AnalysisQueued);
        }

        private async Task SetCurrentInstanceAsAnalyzingAsync(Session activeSession, bool isV2Session)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as Analyzing", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, isV2Session, Status.Analyzing);
        }

        private async Task SetCurrentInstanceStatusAsync(Session activeSession, bool isV2Session, Status sessionStatus)
        {
            try
            {
                await UpdateActiveSessionAsync((latestSessionFromDisk) =>
                {
                    if (latestSessionFromDisk.ActiveInstances == null)
                    {
                        latestSessionFromDisk.ActiveInstances = new List<ActiveInstance>();
                    }

                    var activeInstance = latestSessionFromDisk.GetCurrentInstance();
                    if (activeInstance == null)
                    {
                        activeInstance = new ActiveInstance(Infrastructure.GetInstanceId());
                        latestSessionFromDisk.ActiveInstances.Add(activeInstance);
                    }

                    activeInstance.Status = sessionStatus;
                    return latestSessionFromDisk;
                }, activeSession.SessionId, isV2Session, callerMethodName: "SetCurrentInstanceStatusAsync:" + sessionStatus.ToString());
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent($"Failed while updating current instance status to {sessionStatus}", ex, activeSession.SessionId);
            }
        }

        private void EnsureSessionDirectories()
        {
            _allSessionsDirs.ForEach(x =>
            {
                try
                {
                    FileSystemHelpers.EnsureDirectory(x);
                }
                catch (Exception)
                {
                    // Ignore
                }
            });
        }

        private bool AllInstancesFinished(Session activeSession)
        {
            //
            // Just make sure that the activeSession is not NULL before moving forward. It is possible
            // that another instance ended up completing the session and in that case, we should just
            // assume that the session is complete
            //

            if (activeSession == null)
            {
                return true;
            }

            if (activeSession.ActiveInstances == null)
            {
                return false;
            }

            var completedInstances = activeSession.ActiveInstances.Where(x => x.Status == Status.Complete || x.Status == Status.TimedOut).Select(x => x.Name);

            return Enumerable.SequenceEqual(completedInstances.OrderBy(x => x),
                activeSession.Instances.OrderBy(x => x),
                StringComparer.OrdinalIgnoreCase);
        }

        private async Task MarkSessionAsCompleteAsync(Session activeSession, bool isV2Session, bool forceCompletion = false)
        {
            if (activeSession == null || string.IsNullOrWhiteSpace(activeSession.SessionId))
            {
                return;
            }

            string sessionId = activeSession.SessionId;

            try
            {
                await UpdateActiveSessionAsync((latestSessionFromDisk) =>
                {
                    if (latestSessionFromDisk == null)
                    {
                        return null;
                    }

                    latestSessionFromDisk.Status = forceCompletion ? Status.TimedOut : Status.Complete;
                    latestSessionFromDisk.EndTime = DateTime.UtcNow;
                    return latestSessionFromDisk;

                }, sessionId, isV2Session: isV2Session, callerMethodName: "MarkSessionAsCompleteAsync");

                string activeSessionFile = Path.Combine(isV2Session ? SessionDirectories.ActiveSessionsV2Dir : SessionDirectories.ActiveSessionsDir, sessionId + ".json");
                string completedSessionFile = Path.Combine(isV2Session ? SessionDirectories.CompletedSessionsV2Dir : SessionDirectories.CompletedSessionsDir, sessionId + ".json");

                if (!File.Exists(activeSessionFile))
                {
                    //
                    // Another instance might have moved the file already
                    //

                    return;
                }

                //
                // Move the session file from Active to Complete folder
                //

                Logger.LogSessionVerboseEvent($"Moving session file to completed folder", sessionId);
                FileSystemHelpers.MoveFile(activeSessionFile, completedSessionFile);

                //
                // Clean-up the lock file from the Active session folder
                //

                Logger.LogSessionVerboseEvent($"Cleaning up the lock file from Active session folder", sessionId);
                FileSystemHelpers.DeleteFileSafe(GetActiveSessionLockPath(sessionId, isV2Session));
                Logger.LogSessionVerboseEvent($"Session is complete after {DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes} minutes", sessionId);
                LogMessageToAlertingQueue(activeSession);
            }
            catch (Exception ex)
            {
                //
                // It is possible that multiple instances are trying to mark the session as complete, so
                // ignore any exceptions that come in this code path as some instance will eventually 
                // succeed to update the instance status
                //

                Logger.LogSessionWarningEvent($"Unhandled exception in MarkSessionAsCompleteAsync", ex, sessionId);
            }
        }

        private static void LogMessageToAlertingQueue(Session session)
        {
            try
            {
                var message = new
                {
                    Category = "DiagnosticToolInvoked",
                    session.Tool,
                    TimeStampUtc = DateTime.UtcNow,
                    session.DefaultScmHostName,
                    SessionId = session.SessionId.ToString(),
                    Logs = GetLogsForSession(session)
                };

                _alertingStorageQueue.WriteMessageToAzureQueue(JsonConvert.SerializeObject(message));
            }
            catch (Exception ex)
            {
                Logger.LogSessionWarningEvent("Unhandled exception while writing to AlertingStorageQueue for session", ex, session.SessionId.ToString());
            }
        }

        private static List<string> GetLogsForSession(Session session)
        {
            var files = new List<string>();

            if (session.ActiveInstances == null)
            {
                return files;
            }

            foreach (var activeInstance in session.ActiveInstances)
            {
                foreach (var log in activeInstance.Logs)
                {
                    files.Add(log.Name);
                }
            }

            return files;
        }

        public async Task RunActiveSessionAsync(bool queueAnalysisRequest, CancellationToken cancellationToken)
        {
            var activeSession = await GetActiveSessionAsync(isV2Session: true) ?? throw new Exception("Failed to find the active session");

            if (ShouldCollectOnCurrentInstance(activeSession))
            {
                await SetCurrentInstanceAsStartedAsync(activeSession, isV2Session: true);
                await RunToolForSessionAsync(activeSession, isV2Session: true, queueAnalysisRequest, cancellationToken);
            }
            else
            {
                Logger.LogSessionWarningEvent(
                   $"Current instance [{Environment.MachineName}] is not part of the active session instances ({ GetSessionInstances(activeSession) })",
                   "This session does not belong to this instance",
                   activeSession.SessionId);
            }
        }

        private string GetSessionInstances(Session activeSession)
        {
            if (activeSession == null || activeSession.Instances == null || activeSession.Instances.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(",", activeSession.Instances);
        }

        private string GetDiagLauncherPath()
        {
            string diagLauncherPath = Environment.GetEnvironmentVariable(DaasDiagLauncherEnv);
            if (string.IsNullOrWhiteSpace(diagLauncherPath))
            {
                throw new Exception($"Environment variable {DaasDiagLauncherEnv} not set");
            }

            if (!FileSystemHelpers.FileExists(diagLauncherPath))
            {
                throw new Exception($"DiagLauncher not found at '{diagLauncherPath}'");
            }

            return diagLauncherPath;
        }

        public void ThrowIfMultipleDiagLauncherRunning(int processId = -1)
        {
            string diagLauncherPath = GetDiagLauncherPath();
            string processName = Path.GetFileNameWithoutExtension(diagLauncherPath);

            Process[] processes = processId ==  -1 ? Process.GetProcesses() : Process.GetProcesses().Where(x => x.Id != processId).ToArray();
            foreach (var process in processes)
            {
                string processPath = process.GetMainModuleFileName();
                if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)
                    && processPath.ToLowerInvariant().Contains("daas")
                    && processPath.ToLowerInvariant().Contains("siteextensions"))
                {
                    throw new Exception($"There is already a '{diagLauncherPath}' process running on this instance.");
                }
            }
        }

        public Process StartDiagLauncher(string args, string sessionId, string description)
        {
            return Infrastructure.RunProcess(GetDiagLauncherPath(), args, sessionId, description);
        }

        public Task<StorageAccountValidationResult> ValidateStorageAccount()
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdateStorageAccount(StorageAccount storageAccount)
        {
            throw new NotImplementedException();
        }
    }
}
