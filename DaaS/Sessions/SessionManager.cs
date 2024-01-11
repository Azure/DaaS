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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS.Sessions
{

    public class SessionManager : SessionManagerBase, ISessionManager
    {
        private static readonly AlertingStorageQueue _alertingStorageQueue = new AlertingStorageQueue();
        private static IOperationLock _sessionLockFile;

        private readonly IStorageService _storageService;

        private readonly List<string> _allSessionsDirs = new List<string>()
        {
            SessionDirectories.ActiveSessionsDir,
            SessionDirectories.CompletedSessionsDir
        };

        public SessionManager(IStorageService storageService)
        {
            _storageService = storageService;
            EnsureSessionDirectories();
        }

        #region ISessionManager methods

        public async Task<Session> GetActiveSessionAsync(bool isDetailed = false)
        {
            var activeSessions = await LoadSessionsAsync(SessionDirectories.ActiveSessionsDir, isDetailed, shouldRetry: true);
            var activeSession = activeSessions.FirstOrDefault();
            return activeSession;
        }

        public async Task<IEnumerable<Session>> GetAllSessionsAsync(bool isDetailed = false)
        {
            return (await LoadSessionsAsync(_allSessionsDirs, isDetailed)).OrderByDescending(x => x.StartTime);
        }

        public async Task<IEnumerable<Session>> GetCompletedSessionsAsync()
        {
            return (await LoadSessionsAsync(SessionDirectories.CompletedSessionsDir)).OrderByDescending(x => x.StartTime);
        }

        public async Task<Session> GetSessionAsync(string sessionId, bool isDetailed = false)
        {
            return (await LoadSessionsAsync(_allSessionsDirs, isDetailed))
                .Where(x => x.SessionId == sessionId).FirstOrDefault();
        }

        public async Task<string> SubmitNewSessionAsync(Session session)
        {
            ThrowIfRequiredSettingsMissing();
            string sessionId = string.Empty;

            var activeSession = await GetActiveSessionAsync();
            if (activeSession != null)
            {
                throw new AccessViolationException($"There is already an existing session for {activeSession.Tool}");
            }

            await ThrowIfSessionInvalid(session);

            //
            // Acquire lock on the Active Session file
            //

            var activeSessionLock = await AcquireActiveSessionLockAsync();
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
                var existingActiveSession = await GetActiveSessionAsync();
                if (existingActiveSession == null)
                {
                    //
                    // Save session only if there is no existing active session
                    //

                    sessionId = await SaveSessionAsync(session);
                }
                else
                {
                    existingSessionMessage = $"Existing session '{existingActiveSession.SessionId}' for '{existingActiveSession.Tool}' found";
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

        private async Task ThrowIfSessionInvalid(Session session)
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
                var completedSessions = await GetCompletedSessionsAsync();
                ThrowIfLimitsHitViaAutomation(completedSessions);
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

        public async Task<bool> HasThisInstanceCollectedLogs()
        {
            var activeSession = await GetActiveSessionAsync();
            return activeSession.ActiveInstances != null
                && activeSession.ActiveInstances.Any(x => x.Name.Equals(Infrastructure.GetInstanceId(),
                StringComparison.OrdinalIgnoreCase) && x.Status == Status.Complete);
        }

        public async Task RunToolForSessionAsync(Session activeSession, bool queueAnalysisRequest, CancellationToken token)
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
                    await SetCurrentInstanceAsStartedAsync(activeSession);

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
                    await AppendCollectorResponseToSessionAsync(activeSession, resp);
                }

                await AnalyzeAndCompleteSessionAsync(activeSession, sessionId, token);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Exception while running tool", ex, activeSession.SessionId);
            }
        }

        public async Task AnalyzeAndCompleteSessionAsync(Session activeSession, string sessionId, CancellationToken token)
        {
            //
            // Mark current instance as Analyzing
            //
            await SetCurrentInstanceAsAnalyzingAsync(activeSession);

            //
            // Fire Analysis task
            //
            await AnalyzeAndUpdateSessionAsync(token, sessionId);

            //
            // Mark current instance as Complete
            //
            await SetCurrentInstanceAsCompleteAsync(activeSession);

            //
            // Cleanup all the temporary collected data
            //
            CleanupTempDataForSession(activeSession.SessionId);

            //
            // Check if all the instances have finished running the session
            // and set the Session State to Complete
            //
            await CheckandCompleteSessionIfNeededAsync();
        }

        public async Task<bool> CheckandCompleteSessionIfNeededAsync(bool forceCompletion = false)
        {
            var activeSession = await GetActiveSessionAsync();
            if (activeSession == null)
            {
                return true;
            }

            if (AllInstancesFinished(activeSession) || forceCompletion)
            {
                Logger.LogSessionVerboseEvent("All instances have status as Complete", activeSession.SessionId);
                await MarkSessionAsCompleteAsync(activeSession, forceCompletion: forceCompletion);
                return true;
            }

            return false;
        }

        public async Task<bool> IsSessionExistingAsync(string sessionId)
        {
            var sessionFile = Path.Combine(SessionDirectories.CompletedSessionsDir, sessionId + ".json");
            return FileSystemHelpers.FileExists(sessionFile);
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            EnsureSessionDirectories();

            await Task.Run(async () =>
            {
                var sessionFile = Path.Combine(SessionDirectories.CompletedSessionsDir, sessionId + ".json");
                if (!FileSystemHelpers.FileExists(sessionFile))
                {
                    throw new ArgumentException($"Session {sessionId} does not exist");
                }

                try
                {
                    var session = await FromJsonFileAsync<Session>(sessionFile); ;
                    await DeleteSessionContentsAsync(session, _storageService);

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

        public async Task CancelOrphanedInstancesIfNeeded()
        {
            var activeSession = await GetActiveSessionAsync();
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
            }, activeSession.SessionId, callerMethodName: "CancelOrphanedInstancesIfNeeded");
        }

        #endregion



        private async Task AnalyzeAndUpdateSessionAsync(CancellationToken token, string sessionId)
        {
            var activeSession = await GetActiveSessionAsync();
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

                Logger.LogSessionVerboseEvent("Issuing analysis for session", sessionId);
                await analyzer.AnalyzeLogsAsync(collectedLogs, activeSession, token);

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
            }, activeSession.SessionId, callerMethodName: "AnalyzeAndUpdateSessionAsync");
        }

        private async Task AppendCollectorResponseToSessionAsync(Session activeSession, DiagnosticToolResponse response)
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
                }, activeSession.SessionId, callerMethodName: "AppendCollectorResponseToSessionAsync");
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed in AddLogsToActiveSession", ex, activeSession.SessionId);
            }
        }

        private async Task UpdateActiveSessionAsync(Func<Session, Session> updateSession, string sessionId, string callerMethodName)
        {
            try
            {
                _sessionLockFile = await AcquireSessionLockAsync(sessionId, callerMethodName);

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

                Session latestSessionFromDisk = await GetActiveSessionAsync();
                if (latestSessionFromDisk == null)
                {
                    return;
                }

                Session sessionAfterMergingLatestUpdates = updateSession(latestSessionFromDisk);
                if (sessionAfterMergingLatestUpdates == null)
                {
                    return;
                }

                await UpdateActiveSessionFileAsync(sessionAfterMergingLatestUpdates);
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

        private async Task<List<Session>> LoadSessionsAsync(string directoryToLoadSessionsFrom, bool isDetailed = false, bool shouldRetry = false)
        {
            return await LoadSessionsAsync(new List<string> { directoryToLoadSessionsFrom }, isDetailed, shouldRetry);
        }

        private async Task<List<Session>> LoadSessionsAsync(List<string> directoriesToLoadSessionsFrom, bool isDetailed = false, bool shouldRetry = false)
        {
            EnsureSessionDirectories();
            var sessions = new List<Session>();

            try
            {
                foreach (var directory in directoriesToLoadSessionsFrom)
                {
                    foreach (var sessionFile in FileSystemHelpers.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
                    {
                        await LoadSingleSessionAsync(isDetailed, sessions, sessionFile, shouldRetry);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent($"Failed while loading sessions", ex);
            }

            return sessions;
        }

        private async Task LoadSingleSessionAsync(bool isDetailed, List<Session> sessions, string sessionFile, bool shouldRetry)
        {
            try
            {
                Session session = null;
                if (shouldRetry)
                {
                    await RetryHelper.RetryOnExceptionAsync(5, TimeSpan.FromSeconds(1), async () =>
                    {
                        session = await FromJsonFileAsync<Session>(sessionFile);
                    });
                }
                else
                {
                    session = await FromJsonFileAsync<Session>(sessionFile);
                }

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

        private async Task<string> SaveSessionAsync(Session session)
        {
            try
            {
                session.StartTime = DateTime.UtcNow;
                session.SessionId = GetSessionId(session.StartTime);
                session.Status = Status.Active;

                var diagnoser = GetDiagnoserForSession(session);
                if (diagnoser != null && diagnoser.RequiresStorageAccount)
                {
                    session.BlobStorageHostName = _storageService.GetBlobStorageHostName();
                }

                session.DefaultScmHostName = Settings.Instance.DefaultScmHostName;
                await WriteJsonAsync(session,
                    Path.Combine(SessionDirectories.ActiveSessionsDir, session.SessionId + ".json"));

                LogSessionDetailsSafe(session, isV2Session: false);
                return session.SessionId;
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed while saving the session", ex, session.SessionId);
            }

            return string.Empty;
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

        private async Task<IOperationLock> AcquireSessionLockAsync(string sessionId, string callerMethodName)
        {
            IOperationLock sessionLock = new SessionLockFile(GetActiveSessionLockPath(sessionId));
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

        private async Task<IOperationLock> AcquireActiveSessionLockAsync()
        {
            string lockFilePath = Path.Combine(SessionDirectories.ActiveSessionsDir, "activesession.json.lock");
            IOperationLock sessionLock = new SessionLockFile(lockFilePath);

            Logger.LogSessionVerboseEvent($"Acquiring ActiveSessionLock by AcquireActiveSessionLock", "NEW_SESSION_ID");
            if (sessionLock.Lock("AcquireActiveSessionLock"))
            {
                Logger.LogSessionVerboseEvent($"Acquired ActiveSessionLock by AcquireActiveSessionLock", "NEW_SESSION_ID");
                return sessionLock;
            }

            return null;
        }

        private async Task UpdateActiveSessionFileAsync(Session activeSesion)
        {
            await WriteJsonAsync(
                  activeSesion,
                  Path.Combine(
                      SessionDirectories.ActiveSessionsDir,
                      activeSesion.SessionId + ".json"));
        }

        private string GetActiveSessionLockPath(string sessionId)
        {
            return Path.Combine(
                SessionDirectories.ActiveSessionsDir,
                sessionId + ".json.lock");
        }

        private async Task SetCurrentInstanceAsCompleteAsync(Session activeSession)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as Complete", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, Status.Complete);
        }

        private async Task SetCurrentInstanceAsStartedAsync(Session activeSession)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as Started", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, Status.Started);
        }

        private async Task SetCurrentInstanceAsAnalyzingAsync(Session activeSession)
        {
            Logger.LogSessionVerboseEvent("Setting current instance as Analyzing", activeSession.SessionId);
            await SetCurrentInstanceStatusAsync(activeSession, Status.Analyzing);
        }

        private async Task SetCurrentInstanceStatusAsync(Session activeSession, Status sessionStatus)
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
                }, activeSession.SessionId, callerMethodName: "SetCurrentInstanceStatusAsync:" + sessionStatus.ToString());
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

        private async Task MarkSessionAsCompleteAsync(Session activeSession, bool forceCompletion = false)
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

                }, sessionId, callerMethodName: "MarkSessionAsCompleteAsync");

                string activeSessionFile = Path.Combine(SessionDirectories.ActiveSessionsDir, sessionId + ".json");
                string completedSessionFile = Path.Combine(SessionDirectories.CompletedSessionsDir, sessionId + ".json");

                if (!File.Exists(activeSessionFile))
                {
                    //
                    // Another instance might have moved the file already
                    //

                    return;
                }

                //
                // Move the session file from Active to Complete folder and do this under lock
                //

                _sessionLockFile = await AcquireSessionLockAsync(sessionId, "MarkSessionAsCompleteAsync-MovingFile");
                if (_sessionLockFile == null)
                {
                    //
                    // We failed to acquire the lock on the session file
                    //

                    return;
                }

                try
                {
                    Logger.LogSessionVerboseEvent($"Moving session file to completed folder", sessionId);
                    FileSystemHelpers.MoveFile(activeSessionFile, completedSessionFile);

                }
                catch (Exception exInner)
                {
                    Logger.LogSessionWarningEvent($"Unhandled exception in MarkSessionAsCompleteAsync while moving file", exInner, sessionId);
                }
               
                if (_sessionLockFile != null)
                {
                    Logger.LogSessionVerboseEvent($"SessionLock released by MarkSessionAsCompleteAsync-MovingFile", sessionId);
                    _sessionLockFile.Release();
                }

                //
                // Clean-up the lock file from the Active session folder
                //

                Logger.LogSessionVerboseEvent($"Cleaning up the lock file from Active session folder", sessionId);
                FileSystemHelpers.DeleteFileSafe(GetActiveSessionLockPath(sessionId));
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
    }
}
