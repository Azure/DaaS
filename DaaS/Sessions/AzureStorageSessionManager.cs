// -----------------------------------------------------------------------
// <copyright file="AzureStorageSessionManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.Storage;
using Newtonsoft.Json;

namespace DaaS.Sessions
{
    public class AzureStorageSessionManager : SessionManagerBase, IAzureStorageSessionManager
    {
        internal const string SessionsTableName = "Sessions";
        internal const string ActiveInstancesTableName = "ActiveInstances";

        private TableClient _sessionTableClient;
        private TableClient _activeInstanceTableClient;

        private readonly IStorageService _storageService;

        private string _storedStorageConnectionString = "";

        private bool _isEnabledAndHealthy = false;

        public bool IsEnabled
        {
            get
            {
                string storageConnectionString = Settings.Instance.StorageConnectionString;
                var connectionStringConfigured = !string.IsNullOrWhiteSpace(storageConnectionString);
                if (connectionStringConfigured && HasConnectionStringChanged(storageConnectionString))
                {
                    //
                    // This indicates a change in StorageConnectionString property
                    // Update in-memory structures
                    //

                    if (connectionStringConfigured)
                    {
                        try
                        {
                            _sessionTableClient = new TableClient(storageConnectionString, SessionsTableName);
                            _sessionTableClient.CreateIfNotExists();

                            _activeInstanceTableClient = new TableClient(storageConnectionString, ActiveInstancesTableName);
                            _activeInstanceTableClient.CreateIfNotExists();

                            Logger.LogVerboseEvent($"StorageState got updated successfully. Storage Account used is '{_sessionTableClient.AccountName}'");
                            _isEnabledAndHealthy = true;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogErrorEvent($"Encountered exception while updating storage account connectionstring", ex);
                            _isEnabledAndHealthy = false;
                        }
                    }
                    else
                    {
                        _sessionTableClient = null;
                        _activeInstanceTableClient = null;
                    }

                    _storedStorageConnectionString = storageConnectionString;
                }

                return _isEnabledAndHealthy;
            }
        }

        private bool HasConnectionStringChanged(string updatedStorageConnectionString)
        {
            return _storedStorageConnectionString != updatedStorageConnectionString;
        }

        public AzureStorageSessionManager(IStorageService storageService)
        {
            _storageService = storageService;
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

        private async Task SetCurrentInstanceAsCompleteAsync(Session activeSession)
        {
            await SetCurrentInstanceStatusAsync(activeSession, Status.Complete);
        }

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

                var errors = await CacheLogsToTempFolderAsync(collectedLogs, sessionId, analyzer.RequiresStorageAccount, _storageService);
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

            analyzerErrors = analyzerErrors.Union(GetAnalyzerErrors(activeSession)).ToList();
            Logger.LogSessionVerboseEvent($"Analysis completed. Analysis errors = {string.Join(",", analyzerErrors)}", sessionId);
            await UpdateReportsForSessionAsync(sessionId, Environment.MachineName, collectedLogs, analyzerErrors);
        }

        private async Task SetCurrentInstanceAsAnalyzingAsync(Session activeSession)
        {
            await SetCurrentInstanceStatusAsync(activeSession, Status.Analyzing);
        }

        public async Task<bool> CheckandCompleteSessionIfNeededAsync(bool shouldForciblyTimeout = false)
        {
            if (_sessionTableClient == null)
            {
                throw new NullReferenceException("Azure Table client is not initialized");
            }

            var activeSession = await GetActiveSessionAsync();
            if (activeSession == null)
            {
                return true;
            }

            var activeInstanceEntities = await GetActiveInstanceEntitiesAsync(activeSession.SessionId);
            if (!shouldForciblyTimeout 
                && activeInstanceEntities.Any(x => x.Status == Status.Active.ToString() || x.Status == Status.Analyzing.ToString() || x.Status == Status.AnalysisQueued.ToString()))
            {
                //
                // If we found a single instance in an Active/Analyzing/AnalysisQueued state, return
                //

                return false;
            }

            int instanceRequestedCount = 0;
            if (activeSession.Instances != null)
            {
                instanceRequestedCount = activeSession.Instances.Count;
            }

            if (!shouldForciblyTimeout 
                && activeInstanceEntities.Count() < instanceRequestedCount)
            {
                //
                // If the total of instances in the Table is less than the instances requested,
                // it means that the other instances are yet to to pick up the session so return
                //

                return false;
            }

            var sessionEntity = await GetSessionEntityAsync(activeSession.SessionId);
            if (sessionEntity != null)
            {
                sessionEntity.Status = shouldForciblyTimeout ? Status.TimedOut.ToString(): Status.Complete.ToString();
                sessionEntity.EndTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                await UpdateSessionEntityAsync(sessionEntity);
                Logger.LogSessionVerboseEvent($"Session is complete after {DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes} minutes", activeSession.SessionId);
            }

            return true;
        }

        public async Task<Session> GetActiveSessionAsync(bool isDetailed = false)
        {
            return await GetSessionInternalAsync(sessionId: string.Empty, activeSessionOnly: true, isDetailed: isDetailed);
        }

        public async Task<IEnumerable<Session>> GetAllSessionsAsync(bool isDetailed = false)
        {
            var sessions = new List<Session>();
            var sessionEntities = await GetSessionEntitiesAsync();
            if (!sessionEntities.Any())
            {
                return sessions;
            }

            var activeInstanceEntities = await GetActiveInstanceEntitiesAsync();
            foreach (var sessionEntity in sessionEntities)
            {
                var activeInstancesForSession = activeInstanceEntities.Where(x => x.SessionId == sessionEntity.RowKey);
                var session = SessionFromEntities(sessionEntity, activeInstancesForSession, string.Empty, isDetailed);
                if (session != null)
                {
                    sessions.Add(session);
                }
            }

            return sessions.OrderByDescending(x => x.StartTime).ToList();
        }


        public async Task<Session> GetSessionAsync(string sessionId, bool isDetailed = false)
        {
            return await GetSessionInternalAsync(sessionId, activeSessionOnly: false, isDetailed);
        }

        public async Task RunActiveSessionAsync(bool queueAnalysisRequest, CancellationToken cancellationToken)
        {
            var activeSession = await GetActiveSessionAsync() ?? throw new Exception("Failed to find the active session");
            if (ShouldCollectOnCurrentInstance(activeSession))
            {
                await SetCurrentInstanceAsStartedAsync(activeSession);
                await RunToolForSessionAsync(activeSession, queueAnalysisRequest, cancellationToken);
            }
            else
            {
                Logger.LogSessionWarningEvent(
                   $"Current instance [{Environment.MachineName}] is not part of the active session instances ({GetSessionInstances(activeSession)})",
                   "This session does not belong to this instance",
                   activeSession.SessionId);
            }
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

        private async Task SetCurrentInstanceAsStartedAsync(Session activeSession)
        {
            await SetCurrentInstanceStatusAsync(activeSession, Status.Active);
        }

        public async Task<string> SubmitNewSessionAsync(Session newSession)
        {
            ThrowIfRequiredSettingsMissing();

            if (string.IsNullOrWhiteSpace(newSession.Tool))
            {
                throw new ArgumentException("Please specify a valid diagnostic tool to run");
            }

            if (_sessionTableClient == null)
            {
                throw new NullReferenceException("Azure Table client is not initialized");
            }

            if (string.IsNullOrWhiteSpace(newSession.SessionId))
            {
                throw new NullReferenceException("SessionId cannot be empty for session");
            }

            if (!IsValidSessionId(newSession.SessionId))
            {
                throw new NullReferenceException($"SessionId '{newSession.SessionId}' is invalid");
            }

            await _sessionTableClient.CreateIfNotExistsAsync();

            if (InvokedViaAutomation)
            {
                var completedSessions = await GetCompletedSessionsAsync();
                ThrowIfLimitsHitViaAutomation(completedSessions);
            }

            var activeSession = await GetActiveSessionAsync();
            if (activeSession != null)
            {
                if (activeSession.SessionId == newSession.SessionId)
                {
                    //
                    // Another Kudu instance might have created the session, do nothing
                    // 

                    Logger.LogSessionVerboseEvent("Another instance has already submitted the session", newSession.SessionId);
                    return newSession.SessionId;
                }

                //
                // If the sessionId is different, reject the request to create a new session
                // 

                throw new AccessViolationException($"There is an already an existing active session '{activeSession.SessionId}' for '{activeSession.Tool}'");
            }

            var diagnoser = GetDiagnoserForSession(newSession);
            if (diagnoser != null && diagnoser.RequiresStorageAccount)
            {
                newSession.BlobStorageHostName = _storageService.GetBlobStorageHostName();
            }

            newSession.DefaultScmHostName = Settings.Instance.DefaultScmHostName;

            //
            // If everything ok, create the session entry
            //

            try
            {
                var sessionEntity = new SessionEntity(newSession, GetDefaultHostName());
                await _sessionTableClient.AddEntityAsync(sessionEntity);
                LogSessionDetailsSafe(newSession, isV2Session: true);
                return newSession.SessionId;
            }
            catch (RequestFailedException requestFailedException)
            {
                if (requestFailedException.Status == 409
                    && requestFailedException.ErrorCode == "EntityAlreadyExists")
                {
                    //
                    // It is possible that by the time we reach here, another Kudu instance
                    // ended up submitting the session record successfully. In that case, 
                    // just check if active session's Id is same as this session Id and
                    // ignore the exception else throw an error because the same session
                    // Id could be reused.
                    //

                    if (await IsSameSession(newSession.SessionId))
                    {
                        //
                        // Another Kudu instance might have created the session, do nothing
                        // 

                        Logger.LogSessionVerboseEvent("Caught EntityAlreadyExists exception. Another instance has already submitted the session", newSession.SessionId);
                        return newSession.SessionId;
                    }

                    //
                    // For existing sessions, Azure Table storage will return the following error
                    // The specified entity already exists.
                    // Status: 409(Conflict)
                    // ErrorCode: EntityAlreadyExists
                    //

                    throw new AccessViolationException($"An existing session with the same SessionId '{newSession.SessionId}' already exists");
                }

                throw;
            }

            throw new Exception("Failed to submit session");
        }

        private bool IsValidSessionId(string sessionId)
        {
            return DateTime.TryParseExact(sessionId, "yyMMdd_HHmmssffff", null, System.Globalization.DateTimeStyles.None, out DateTime _);
        }

        private string GetDefaultHostName()
        {
            string defaultHostName = Settings.Instance.DefaultScmHostName;
            if (string.IsNullOrWhiteSpace(defaultHostName))
            {
                throw new Exception("Failed to get the HostName for the app. Please try again later.");
            }

            return defaultHostName.Replace(".scm.", ".");
        }

        public Process StartDiagLauncher(string args, string sessionId, string description)
        {
            return Infrastructure.RunProcess(GetDiagLauncherPath(), args, sessionId, description);
        }

        public void ThrowIfMultipleDiagLauncherRunning(int processId)
        {
            string diagLauncherPath = GetDiagLauncherPath();
            string processName = Path.GetFileNameWithoutExtension(diagLauncherPath);

            Process[] processes = processId == -1 ? Process.GetProcesses() : Process.GetProcesses().Where(x => x.Id != processId).ToArray();
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

                if (queueAnalysisRequest)
                {
                    await SetCurrentInstanceAsAnalysisQueuedAsync(activeSession);
                    return;
                }

                await AnalyzeAndCompleteSessionAsync(activeSession, sessionId, token);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Exception while running tool", ex, activeSession.SessionId);
            }
        }

        private async Task AppendCollectorResponseToSessionAsync(Session activeSession, DiagnosticToolResponse resp)
        {
            await UpdateLogsForSessionAsync(activeSession.SessionId, Environment.MachineName, resp.Logs, resp.Errors);
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

        private async Task<Session> GetActiveSessionAsync()
        {
            return await GetSessionInternalAsync(sessionId: string.Empty, activeSessionOnly: true);
        }

        private async Task<Session> GetSessionInternalAsync(string sessionId = "", bool activeSessionOnly = false, bool isDetailed = false)
        {
            var sessionEntity = await GetSessionEntityAsync(sessionId, activeSessionOnly);
            if (sessionEntity == null)
            {
                return null;
            }

            if (activeSessionOnly)
            {
                sessionId = sessionEntity.RowKey;
            }

            var activeInstanceEntities = await GetActiveInstanceEntitiesAsync(sessionId);
            return SessionFromEntities(sessionEntity, activeInstanceEntities, sessionId, isDetailed);
        }

        private async Task<SessionEntity> GetSessionEntityAsync(string sessionId, bool activeSessionOnly = false)
        {
            var sessionEntities = await GetSessionEntitiesAsync(sessionId, activeSessionOnly);
            return sessionEntities.FirstOrDefault();
        }

        private async Task<IEnumerable<SessionEntity>> GetSessionEntitiesAsync(string sessionId = "", bool activeSessionOnly = false)
        {
            var sessionEntities = new List<SessionEntity>();
            if (_sessionTableClient == null)
            {
                return sessionEntities;
            }

            AsyncPageable<SessionEntity> sessionEntitiesAsync;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                sessionEntitiesAsync = _sessionTableClient.QueryAsync<SessionEntity>(x => x.PartitionKey == GetDefaultHostName() && x.RowKey == sessionId);
            }
            else if (activeSessionOnly)
            {
                sessionEntitiesAsync = _sessionTableClient.QueryAsync<SessionEntity>(x => x.PartitionKey == GetDefaultHostName() && x.Status == Status.Active.ToString());
            }
            else
            {
                sessionEntitiesAsync = _sessionTableClient.QueryAsync<SessionEntity>(x => x.PartitionKey == GetDefaultHostName());
            }

            await foreach (var e in sessionEntitiesAsync)
            {
                sessionEntities.Add(e);
            }

            return sessionEntities;
        }

        private async Task<ActiveInstanceEntity?> GetActiveInstanceEntityAsync(string sessionId, string instanceName)
        {
            var activeInstanceEntities = await GetActiveInstanceEntitiesAsync(sessionId, instanceName);
            return activeInstanceEntities.FirstOrDefault();
        }
        private async Task<IEnumerable<ActiveInstanceEntity>> GetActiveInstanceEntitiesAsync(string sessionId = "", string instanceName = "")
        {
            var activeInstanceEntities = new List<ActiveInstanceEntity>();
            if (_activeInstanceTableClient == null)
            {
                return activeInstanceEntities;
            }

            AsyncPageable<ActiveInstanceEntity> activeInstanceEntitiesAsync;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                if (!string.IsNullOrWhiteSpace(instanceName))
                {
                    activeInstanceEntitiesAsync = _activeInstanceTableClient
                        .QueryAsync<ActiveInstanceEntity>(x => x.PartitionKey == GetDefaultHostName() && x.RowKey == $"{sessionId}_{instanceName.ToLowerInvariant()}");
                }
                else
                {
                    activeInstanceEntitiesAsync = _activeInstanceTableClient
                        .QueryAsync<ActiveInstanceEntity>(x => x.PartitionKey == GetDefaultHostName() && x.SessionId == sessionId);
                }
            }
            else
            {
                activeInstanceEntitiesAsync = _activeInstanceTableClient
                    .QueryAsync<ActiveInstanceEntity>(x => x.PartitionKey == GetDefaultHostName());
            }

            await foreach (var e in activeInstanceEntitiesAsync)
            {
                activeInstanceEntities.Add(e);
            }

            return activeInstanceEntities;
        }

        private Session SessionFromEntities(SessionEntity sessionEntity, IEnumerable<ActiveInstanceEntity> activeInstanceEntities, string sessionId, bool isDetailed)
        {
            if (sessionEntity == null)
            {
                return null;
            }

            var session = new Session()
            {
                SessionId = sessionEntity.RowKey,
                StartTime = sessionEntity.StartTime,
                Tool = sessionEntity.Tool,
                ToolParams = sessionEntity.ToolParams,
                EndTime = sessionEntity.EndTime,
                Description = sessionEntity.Description,
                DefaultScmHostName = sessionEntity.PartitionKey,
                BlobStorageHostName = sessionEntity.BlobStorageHostName
            };

            var diagnoser = GetDiagnoserForSession(sessionEntity.Tool);

            if (!string.IsNullOrWhiteSpace(sessionEntity.InstancesJson))
            {
                var instances = JsonConvert.DeserializeObject<List<string>>(sessionEntity.InstancesJson);
                if (instances != null && instances.Any())
                {
                    session.Instances = instances;
                }
            }

            if (Enum.TryParse(sessionEntity.Status, out Status status))
            {
                session.Status = status;
            }

            if (Enum.TryParse(sessionEntity.Mode, out Mode mode))
            {
                session.Mode = mode;
            }

            foreach (var instanceEntity in activeInstanceEntities)
            {
                var activeInstance = ActiveInstanceFromEntity(instanceEntity, sessionId, diagnoser.RequiresStorageAccount);
                if (activeInstance != null)
                {
                    session.ActiveInstances ??= new List<ActiveInstance>();
                    session.ActiveInstances.Add(activeInstance);
                }
            }

            if (isDetailed && session.Status != Status.Complete && session.Status != Status.TimedOut)
            {
                UpdateCollectorStatus(session);
                UpdateAnalyzerStatus(session);
            }

            return session;
        }

        private ActiveInstance ActiveInstanceFromEntity(ActiveInstanceEntity instanceEntity, string sessionId, bool diagnoserRequiresStorage)
        {
            if (instanceEntity == null || string.IsNullOrWhiteSpace(instanceEntity.InstanceName))
            {
                return null;
            }

            var instance = new ActiveInstance(instanceEntity.InstanceName);

            if (Enum.TryParse(instanceEntity.Status, out Status status))
            {
                instance.Status = status;
            }

            UpdateErrorsForInstance(instanceEntity, sessionId, instance);
            UpdateLogsForInstance(instanceEntity, sessionId, instance, diagnoserRequiresStorage);

            return instance;
        }

        private void UpdateErrorsForInstance(ActiveInstanceEntity instanceEntity, string sessionId, ActiveInstance instance)
        {
            if (!string.IsNullOrWhiteSpace(instanceEntity.CollectorErrorsJson))
            {
                try
                {
                    var errors = JsonConvert.DeserializeObject<List<string>>(instanceEntity.CollectorErrorsJson);
                    if (errors != null && errors.Any())
                    {
                        instance.CollectorErrors = errors;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogSessionErrorEvent("Failed while deserializing CollectorErrorsJson", ex, sessionId);
                }
            }

            if (!string.IsNullOrWhiteSpace(instanceEntity.AnalyzerErrorsJson))
            {
                try
                {
                    var errors = JsonConvert.DeserializeObject<List<string>>(instanceEntity.AnalyzerErrorsJson);
                    if (errors != null && errors.Any())
                    {
                        instance.AnalyzerErrors = errors;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogSessionErrorEvent("Failed while AnalyzerErrorsJson ErrorsJson", ex, sessionId);
                }
            }
        }

        private void UpdateLogsForInstance(
            ActiveInstanceEntity instanceEntity,
            string sessionId,
            ActiveInstance instance,
            bool diagnoserRequiresStorage)
        {
            if (!string.IsNullOrWhiteSpace(instanceEntity.LogFilesJson))
            {
                try
                {
                    var logs = JsonConvert.DeserializeObject<List<LogFile>>(instanceEntity.LogFilesJson);
                    if (logs != null && logs.Any())
                    {
                        foreach (var log in logs)
                        {
                            if (IncludeSasUri)
                            {
                                if (diagnoserRequiresStorage)
                                {
                                    log.RelativePath = GetPathWithSasUri(log.PartialPath);
                                }
                                else
                                {
                                    log.RelativePath = $"{Utility.GetScmHostName()}/api/vfs/{log.PartialPath}";
                                }
                            }

                            foreach (var report in log.Reports)
                            {
                                if (!string.IsNullOrWhiteSpace(report.RelativePath))
                                {
                                    report.RelativePath = report.RelativePath;
                                }
                            }

                            log.Reports = SanitizeReports(log.Reports);
                        }

                        instance.Logs = logs;
                    }


                }
                catch (Exception ex)
                {
                    Logger.LogSessionErrorEvent("Failed while deserializing LogFilesJson", ex, sessionId);
                }
            }
        }

        private async Task<bool> IsSameSession(string newSessionId)
        {
            try
            {
                var activeSession = await GetActiveSessionAsync();
                if (activeSession != null && activeSession.SessionId == newSessionId)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent($"Failed while checking the active session", ex, newSessionId);
            }

            return false;
        }

        private async Task SetCurrentInstanceStatusAsync(Session activeSession, Status status)
        {
            if (string.IsNullOrWhiteSpace(activeSession.SessionId))
            {
                throw new NullReferenceException("SessionId for the current session is empty");
            }

            Logger.LogSessionVerboseEvent($"Setting current instance as {status}", activeSession.SessionId);
            await SetIntanceStatus(activeSession.SessionId, status);
        }

        private async Task SetIntanceStatus(string sessionId, Status status)
        {
            if (_activeInstanceTableClient == null)
            {
                throw new NullReferenceException("Azure Table client is not initialized");
            }

            var activeInstanceEntity = await GetActiveInstanceEntityAsync(sessionId, Environment.MachineName);
            if (activeInstanceEntity == null)
            {
                var activeInstanceActivity = new ActiveInstanceEntity()
                {
                    PartitionKey = GetDefaultHostName(),
                    RowKey = $"{sessionId}_{Environment.MachineName.ToLowerInvariant()}",
                    SessionId = sessionId,
                    InstanceName = Environment.MachineName.ToLowerInvariant()
                };

                activeInstanceActivity.Status = status.ToString();
                await _activeInstanceTableClient.AddEntityAsync(activeInstanceActivity);
                return;
            }

            activeInstanceEntity.Status = status.ToString();
            await UpdateActiveInstanceEntityAsync(activeInstanceEntity);
        }

        private async Task UpdateLogsForSessionAsync(string sessionId, string instanceName, List<LogFile> logs, List<string> errors)
        {
            if (_activeInstanceTableClient == null)
            {
                throw new NullReferenceException("Azure Table client is not initialized");
            }

            var activeInstanceEntity = await GetActiveInstanceEntityAsync(sessionId, instanceName);
            if (activeInstanceEntity == null)
            {
                throw new InvalidOperationException($"Instance '{instanceName}' for session '{sessionId}' not found");
            }

            activeInstanceEntity.LogFilesJson = JsonConvert.SerializeObject(logs);
            activeInstanceEntity.CollectorErrorsJson = JsonConvert.SerializeObject(errors);
            await UpdateActiveInstanceEntityAsync(activeInstanceEntity);
        }

        private async Task UpdateReportsForSessionAsync(string sessionId, string instanceName, List<LogFile> logs, List<string> errors)
        {
            if (_activeInstanceTableClient == null)
            {
                throw new NullReferenceException("Azure Table client is not initialized");
            }

            var activeInstanceEntity = await GetActiveInstanceEntityAsync(sessionId, instanceName);
            if (activeInstanceEntity == null)
            {
                throw new InvalidOperationException($"Instance '{instanceName}' for session '{sessionId}' not found");
            }

            activeInstanceEntity.LogFilesJson = JsonConvert.SerializeObject(logs);
            activeInstanceEntity.AnalyzerErrorsJson = JsonConvert.SerializeObject(errors);
            await UpdateActiveInstanceEntityAsync(activeInstanceEntity);
        }

        private async Task UpdateActiveInstanceEntityAsync(ActiveInstanceEntity activeInstanceEntity)
        {
            if (_activeInstanceTableClient == null)
            {
                return;
            }

            await _activeInstanceTableClient.UpdateEntityAsync(activeInstanceEntity, activeInstanceEntity.ETag);
        }

        private async Task SetCurrentInstanceAsAnalysisQueuedAsync(Session activeSession)
        {
            await SetCurrentInstanceStatusAsync(activeSession, Status.AnalysisQueued);
        }

        public async Task<IEnumerable<Session>> GetCompletedSessionsAsync()
        {
            var sessions = await GetAllSessionsAsync();
            return sessions.Where(x=>x.Status != Status.Active);
        }

        public Task<bool> HasThisInstanceCollectedLogs()
        {
            throw new NotImplementedException();
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            if (_sessionTableClient == null || _activeInstanceTableClient == null)
            {
                throw new Exception("Failed while deleting session. Storage credentials are not specified correctly.");
            }

            try
            {
                var session = await GetSessionAsync(sessionId);
                if (session == null)
                {
                    Logger.LogSessionWarningEvent("Failed to Delete session", "Failed to get session information from Storage", sessionId);
                }

                if (session.Status == Status.Active)
                {
                    throw new InvalidOperationException($"Session '{sessionId}' is active. Cannot delete an active session.");
                }

                Logger.LogSessionVerboseEvent("Deleting session", sessionId);
                await DeleteSessionContentsAsync(session, _storageService);

                var activeInstanceEntities = await GetActiveInstanceEntitiesAsync(sessionId);
                foreach (var entity in activeInstanceEntities)
                {
                    await _activeInstanceTableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey);
                }

                var sessionEntity = await GetSessionEntityAsync(sessionId);
                if (sessionEntity == null)
                {
                    throw new Exception($"Session with Id '{sessionId}' not found");
                }

                await _sessionTableClient.DeleteEntityAsync(sessionEntity.PartitionKey, sessionEntity.RowKey);
                Logger.LogSessionVerboseEvent("Session deleted", sessionId);
            }
            catch (Exception ex)
            {
                Logger.LogSessionWarningEvent($"Failed while deleting session '{sessionId}'", ex, sessionId);
                throw new Exception($"Failed while deleting session '{sessionId}' with error - {ex.Message} ");
            }
        }

        public async Task<bool> IsSessionExistingAsync(string sessionId)
        {
            var session = await GetSessionAsync(sessionId);
            return session != null;
        }

        public async Task<bool> CancelOrphanedV2InstancesIfNeeded(Session activeSession)
        {
            if (DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes < Settings.Instance.OrphanInstanceTimeoutInMinutes)
            {
                return false;
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
                return false;
            }

            bool isSessionUpdated = false;

            try
            {
                Logger.LogSessionErrorEvent("Identified orphaned instances for session",
                    $"Orphaning instance(s) {string.Join(",", orphanedInstanceNames)} as they haven't picked up the session",
                    activeSession.SessionId);

                var orphanedInstances = new List<ActiveInstance>();
                foreach (var instance in orphanedInstanceNames)
                {
                    var activeInstanceEntity = await GetActiveInstanceEntityAsync(activeSession.SessionId, instance);
                    if (activeInstanceEntity != null)
                    {
                        var collectorErrors = new List<string>();
                        if (!string.IsNullOrWhiteSpace(activeInstanceEntity.CollectorErrorsJson))
                        {
                            var existingErrors = JsonConvert.DeserializeObject<List<string>>(activeInstanceEntity.CollectorErrorsJson);
                            collectorErrors.Union(existingErrors);
                        }

                        collectorErrors.Add($"The instance [{instance}] did not pick up the session within the required time");
                        await UpdateActiveInstanceEntityAsync(activeInstanceEntity);
                        isSessionUpdated = true;
                    }
                }

                try
                {
                    var isComplete = await CheckandCompleteSessionIfNeededAsync();
                    if (isComplete)
                    {
                        isSessionUpdated = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarningEvent("Failed while updating session", ex);
                }
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed while updating orphaned instances for the session", ex, activeSession.SessionId);
            }

            return isSessionUpdated;
        }

        private async Task UpdateSessionEntityAsync(SessionEntity sessionEntity)
        {
            if (_sessionTableClient == null)
            {
                return;
            }

            //
            // For this UpdateEntityAsync call, we are using ETag.all to prevent
            // operation failing with a status of 412 (Precondition Failed). Multiple
            // instances could be modifying the Session to TimedOut or Complete and for
            // this case, we are fine with the Last Writer wins to avoid the exception.
            //

            await _sessionTableClient.UpdateEntityAsync(sessionEntity, ETag.All);
        }

        public async Task <bool> ShouldSessionTimeoutAsync(Session activeSession)
        {
            if (activeSession == null)
            {
                return true;
            }

            if (CheckIfTimeLimitExceeded(activeSession))
            {
                Logger.LogSessionErrorEvent("Allowed time limit exceeded for the session", $"Session was started at {activeSession.StartTime}", activeSession.SessionId);
                await CheckandCompleteSessionIfNeededAsync(shouldForciblyTimeout: true);
                return true;
            }

            return false;
        }

        public Task CancelOrphanedInstancesIfNeeded()
        {
            throw new NotImplementedException();
        }
    }
}
