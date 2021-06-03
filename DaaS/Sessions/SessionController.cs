//-----------------------------------------------------------------------
// <copyright file="SessionController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.HeartBeats;
using DaaS.Storage;
using System.Diagnostics;
using System.Threading;
using System.Text;

namespace DaaS.Sessions
{
    public class TaskAndCancellationToken
    {
        public Task UnderlyingTask { get; set; }
        public CancellationTokenSource CancellationSource { get; set; }
    }
    public class SessionController
    {
        private static readonly object _daasVersionUpdateLock = new object();
        private static bool _daasVersionCheckInProgress = false;

        public string BlobStorageSasUri
        {
            get
            {
                return Infrastructure.Settings.BlobStorageSas;
            }
            set
            {
                Infrastructure.Settings.BlobStorageSas = value;
            }
        }

        public string GetBlobSasUriFromEnvironment(out bool definedAsEnvironmentVariable)
        {
            var blobSasUri = Settings.GetBlobSasUriFromEnvironment(out definedAsEnvironmentVariable);
            return blobSasUri;
        }

        public TimeSpan FrequencyToCheckForNewSessionsAt
        {
            get
            {
                return Infrastructure.Settings.FrequencyToCheckForNewSessionsAt;
            }
        }
        public int MaxDiagnosticSessionsToKeep
        {
            get
            {
                return Infrastructure.Settings.MaxDiagnosticSessionsToKeep;
            }
        }
        public int MaxNumberOfDaysForSessions
        {
            get
            {
                return Infrastructure.Settings.MaxNumberOfDaysForSessions;
            }
        }

        private List<string> _allSessionsDirs = new List<string>()
            {
                SessionDirectories.ActiveSessionsDir,
                SessionDirectories.CollectedLogsOnlySessionsDir,
                SessionDirectories.CompletedSessionsDir
            };

        public Session CollectLogs(
            DateTime utcStartTime,
            DateTime utcEndTime,
            List<Diagnoser> diagnosers,
            bool invokedViaDaasConsole = false,
            List<Instance> instancesToRunOn = null,
            string description = null,
            string blobSasUri = "")
        {
            var session = CreateSession(utcStartTime, utcEndTime, SessionType.Collect, diagnosers, invokedViaDaasConsole, instancesToRunOn, description, blobSasUri);
            return session;
        }

        public Session CollectLiveDataLogs(
            TimeSpan timeSpan,
            List<Diagnoser> diagnosers,
             bool invokedViaDaasConsole = false,
             List<Instance> instancesToRunOn = null,
            string description = null,
            string blobSasUri = "")
        {
            DateTime utcStartTime = DateTime.UtcNow;
            DateTime utcEndTime = utcStartTime + timeSpan;
            return CollectLogs(utcStartTime, utcEndTime, diagnosers, invokedViaDaasConsole, instancesToRunOn, description, blobSasUri);
        }

        public Session Analyze(Session session)
        {
            session.AnalyzeLogs();
            return session;
        }

        public Session Cancel(Session session)
        {
            session.CancelSessionAsync().Wait();
            return session;
        }

        public void CancelActiveSessionOnThisInstance(string sessionId)
        {
            var sessionToCancel = new SessionId(sessionId);
            if (_runningSessions.ContainsKey(sessionToCancel))
            {
                Logger.LogSessionVerboseEvent("Found a session to cancel on this instance", sessionId);
                var runningSessionTaskAndSource = _runningSessions[sessionToCancel];
                if (runningSessionTaskAndSource.CancellationSource != null)
                {
                    runningSessionTaskAndSource.CancellationSource.Cancel();
                    Logger.LogSessionVerboseEvent($"Active task for session {sessionId} has been cancelled", sessionId);
                }
            }
        }

        public Session Troubleshoot(
            DateTime utcStartTime,
            DateTime utcEndTime,
            List<Diagnoser> diagnosers,
            bool invokedViaDaasConsole = false,
            List<Instance> instancesToRunOn = null,
            string description = null,
            string blobSasUri = "")
        {
            var session = CreateSession(utcStartTime, utcEndTime, SessionType.Diagnose, diagnosers, invokedViaDaasConsole, instancesToRunOn, description, blobSasUri);
            return session;
        }

        public Session TroubleshootLiveData(
            TimeSpan timeSpan,
            List<Diagnoser> diagnosers,
            bool invokedViaDaasConsole = false,
            List<Instance> instancesToRunOn = null,
            string description = null,
            string blobSasUri = "")
        {
            DateTime utcStartTime = DateTime.UtcNow;
            DateTime utcEndTime = utcStartTime + timeSpan;
            return Troubleshoot(utcStartTime, utcEndTime, diagnosers, invokedViaDaasConsole, instancesToRunOn, description, blobSasUri);
        }

        private Session CreateSession(
            DateTime utcStartTime,
            DateTime utcEndTime,
            SessionType sessionType,
            List<Diagnoser> diagnosers,
            bool invokedViaDaasConsole = false,
            List<Instance> instancesToRun = null,
            string description = null,
            string blobSasUri = "")
        {
            bool sasUriInEnvironmentVariable = false;
            bool sandboxAvailable = true;

            var daasDisabled = Environment.GetEnvironmentVariable("WEBSITE_DAAS_DISABLED");
            if (daasDisabled != null && daasDisabled.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                throw new AccessViolationException("DaaS is disabled for this Web App. Enable it by removing the WEBSITE_DAAS_DISABLED AppSetting or by setting it to True");
            }

            var computeMode = Environment.GetEnvironmentVariable("WEBSITE_COMPUTE_MODE");
            if (computeMode != null && !computeMode.Equals("Dedicated", StringComparison.OrdinalIgnoreCase))
            {
                throw new AccessViolationException("DaaS is only supported on websites running in Basic or Standard mode");
            }


            //
            // If a SAS URI is specified at the session level, that takes precedence over 
            // every other place
            //
            if (!string.IsNullOrWhiteSpace(blobSasUri))
            {
                if (!BlobController.ValidateBlobSasUri(blobSasUri, out Exception exceptionStorage))
                {
                    throw new ApplicationException($"BlobSasUri specified is invalid. Failed with error - {exceptionStorage.Message}");
                }
            }

            //
            // If no SAS URI is specfied, check if the diagnosers requires a storage account. If yes, 
            // we need to check if the SAS URI is configured an an environment variable and populate 
            // it here
            //
            if (string.IsNullOrWhiteSpace(blobSasUri)
                && diagnosers.Any(x => x.DiagnoserRequiresStorage)
                && Settings.IsBlobSasUriConfiguredAsEnvironmentVariable())
            {
                //
                // This call is required so that the container gets created if it does not exist so far
                //
                
                if (!BlobController.ValidateBlobSasUri(Settings.WebSiteDaasStorageSasUri, out Exception exceptionStorage))
                {
                    throw new ApplicationException($"BlobSasUri specified in environment variable is invalid. Failed with error - {exceptionStorage.Message}");
                }

                if (Settings.IsSandBoxAvailable())
                {
                    blobSasUri = Settings.WebSiteDaasStorageSasUri;
                    sasUriInEnvironmentVariable = true;
                }
                else
                {
                    //
                    // This is the case for RPS Enabled ASE environments. If Sandbox is not available
                    // we need to save the SAS URI in the session file so that DaasRunner on other instances
                    // can access the SAS URI. This is required because setting the SAS URI *does not*
                    // restart the site and the DAAS Runner may not be able to get the SAS URI via the
                    // sandbox property
                    //

                    blobSasUri = GetBlobSasUriFromEnvironment(out _);
                    sasUriInEnvironmentVariable = true;
                    sandboxAvailable = false;
                }
                
            }

            // Make sure there is no Active Session for any of the
            // Diagnosers specified for this session
            foreach (var activeSession in GetAllActiveSessions())
            {
                foreach (var diagnoserSession in activeSession.GetDiagnoserSessions())
                {
                    if (diagnosers.Any(x => x.Name == diagnoserSession.Diagnoser.Name))
                    {
                        throw new AccessViolationException($"There is already another session for {diagnoserSession.Diagnoser.Name} in progress so cannot submit a new session");
                    }
                }
            }

            if (invokedViaDaasConsole)
            {
                var maxSessionsPerDay = Infrastructure.Settings.MaxSessionsPerDay;
                var existingSessions = GetAllInActiveSessions().Where(x => x.StartTime > DateTime.UtcNow.AddDays(-1) && CheckDiagnoserSessions(x, diagnosers)).Count();

                Logger.LogVerboseEvent($"Existing session count is {existingSessions} and maxSessionsPerDay = {maxSessionsPerDay}");

                if (existingSessions > maxSessionsPerDay)
                {
                    throw new AccessViolationException($"The limit of maximum number of DaaS sessions ({maxSessionsPerDay} per day) has been reached. Either disable the autohealing rule, delete existing sessions or increase MaxSessionsPerDay setting in \\home\\data\\daas\\PrivateSettings.xml file. (Changing MaxSessionsPerDay setting, requires a restart of the Kudu Site)");
                }

                var sessionThresholdPeriodInMinutes = Infrastructure.Settings.MaxSessionCountThresholdPeriodInMinutes;
                var maxSessionCountInThresholdPeriod = Infrastructure.Settings.MaxSessionCountInThresholdPeriod;

                var sessionsInThresholdPeriod = GetAllInActiveSessions().Where(x => x.StartTime > DateTime.UtcNow.AddMinutes(-1 * sessionThresholdPeriodInMinutes)).Count();
                if (sessionsInThresholdPeriod >= maxSessionCountInThresholdPeriod)
                {
                    throw new AccessViolationException($"To avoid impact to application and disk space, a new DaaS session request is rejected as a total of {maxSessionCountInThresholdPeriod} DaaS sessions were submitted in the last {sessionThresholdPeriodInMinutes} minutes ");
                }
                else
                {
                    Logger.LogVerboseEvent($"MaxSessionCountThresholdPeriodInMinutes is {sessionThresholdPeriodInMinutes} and sessionsInThresholdPeriod is {sessionsInThresholdPeriod} so allowing DaaSConsole to submit a new session");
                }
            }

            var session = new Session(diagnosers, utcStartTime, utcEndTime, sessionType, instancesToRun)
            {
                Description = description,
                BlobSasUri = blobSasUri,
                BlobStorageHostName = BlobController.GetBlobStorageHostName(blobSasUri),
                DefaultHostName = Settings.DefaultHostName
            };
            session.Save();
            Logger.LogNewSession(session.SessionId.ToString(),
                sessionType.ToString(),
                string.Join(",", diagnosers.Select(x => x.Name)),
                string.Join(",", session.InstancesSpecified.Select(x => x.Name).ToArray()),
                invokedViaDaasConsole,
                !string.IsNullOrWhiteSpace(session.BlobSasUri),
                sasUriInEnvironmentVariable,
                sandboxAvailable,
                session.DefaultHostName);

            return session;
        }

        private bool CheckDiagnoserSessions(Session session, List<Diagnoser> diagnosers)
        {
            foreach (var diagnoserSession in session.GetDiagnoserSessions())
            {
                if (diagnosers.Any(x => x.Name == diagnoserSession.Diagnoser.Name))
                {
                    return true;
                }
            }

            return false;
        }

        private List<Session> LoadSessionsFromStorage(List<string> directoriesToLoadSessionsFrom)
        {
            var sessions = new List<Session>();

            foreach (var directory in directoriesToLoadSessionsFrom)
            {
                try
                {
                    var sessionFilePaths = Infrastructure.Storage.GetFilesInDirectory(directory,
                        Session.GetSessionStorageLocation(), string.Empty, "*.xml", SearchOption.TopDirectoryOnly);

                    foreach (var sessionFilePath in sessionFilePaths)
                    {
                        FileInfo f = new FileInfo(sessionFilePath);
                        if (f.Exists && f.Length == 0)
                        {
                            Logger.LogSessionVerboseEvent(Path.GetFileNameWithoutExtension(f.Name), $"Session File '{sessionFilePath}' has zero length. We will delete this session file");
                            RetryHelper.RetryOnException("Deleting session file of zero length...", () =>
                            {
                                System.IO.File.Delete(sessionFilePath);
                            }, TimeSpan.FromSeconds(1));
                        }
                        else
                        {
                            using (
                                var sessionFileContentStream = Infrastructure.Storage.ReadFile(sessionFilePath,
                                Session.GetSessionStorageLocation()))
                            {
                                var session = new Session(sessionFileContentStream, sessionFilePath);
                                sessions.Add(session);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.LogErrorEvent("Encountered unhandled exception while loading session from file", e);
                }
            }

            return sessions;
        }

        public IEnumerable<Session> GetAllSessions()
        {
            var sessions = LoadSessionsFromStorage(_allSessionsDirs);
            return sessions;
        }

        public IEnumerable<Session> GetAllActiveSessions()
        {
            var activeSessionsDir = new List<string>() { SessionDirectories.ActiveSessionsDir };
            var activeSessions = LoadSessionsFromStorage(activeSessionsDir);
            return activeSessions;
        }

        public IEnumerable<Session> GetAllUnanalyzedSessions()
        {
            var sessionsDir = new List<string>() { SessionDirectories.CollectedLogsOnlySessionsDir };
            var sessions = LoadSessionsFromStorage(sessionsDir);
            return sessions;
        }

        public IEnumerable<Session> GetAllCompletedSessions()
        {
            var completedSessionsDir = new List<string>() { SessionDirectories.CompletedSessionsDir };
            var completedSessions = LoadSessionsFromStorage(completedSessionsDir);
            return completedSessions;
        }

        public IEnumerable<Session> GetAllInActiveSessions()
        {
            var inactiveSessionsDir = new List<string>() { SessionDirectories.CompletedSessionsDir, SessionDirectories.CollectedLogsOnlySessionsDir };
            var inActiveSessions = LoadSessionsFromStorage(inactiveSessionsDir);
            return inActiveSessions;
        }

        public List<CancelledInstance> GetCancelledInstances()
        {
            List<CancelledInstance> cancelledInstances = new List<CancelledInstance>();
            foreach (var file in Infrastructure.Storage.GetFilesInDirectory(Settings.CancelledDir, StorageLocation.UserSiteData, string.Empty))
            {
                try
                {
                    using (Stream stream = Infrastructure.Storage.ReadFile(file, StorageLocation.UserSiteData))
                    {
                        CancelledInstance cancelledInstance = stream.LoadFromXmlStream<CancelledInstance>();
                        cancelledInstances.Add(cancelledInstance);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogErrorEvent("Failed while retrieving cancelled instance information", ex);
                    try
                    {
                        Infrastructure.Storage.DeleteFileAsync(file);
                    }
                    catch (Exception exInner)
                    {
                        Logger.LogErrorEvent($"Failed while deleting cancelled instance file {file} with exception", exInner);
                    }
                }

            }
            return cancelledInstances;
        }

        public Session GetSessionWithId(SessionId sessionId)
        {
            foreach (var dir in _allSessionsDirs)
            {
                var sessionFileName = sessionId + SessionConstants.SessionFileExtension;
                var potentialSessionPath = Path.Combine(dir, sessionFileName);
                if (Infrastructure.Storage.FileExists(potentialSessionPath, Session.GetSessionStorageLocation()))
                {
                    int retryCount = 0;

                    Exception exToThrow = null;
                retryLabel:
                    if (retryCount > 2)
                    {
                        if (exToThrow != null)
                        {
                            throw exToThrow;
                        }
                        else
                        {
                            Logger.LogVerboseEvent($"Failed reading the session file {potentialSessionPath} after 3 retries...giving up");
                            throw new Exception("Failed to read the session file after 3 retries");
                        }
                    }

                    try
                    {
                        using (var sessionFileContentStream = Infrastructure.Storage.ReadFile(potentialSessionPath, Session.GetSessionStorageLocation()))
                        {
                            var session = new Session(sessionFileContentStream, potentialSessionPath);
                            return session;
                        }
                    }
                    catch (IOException ioException)
                    {
                        retryCount++;
                        exToThrow = ioException;
                        Logger.LogVerboseEvent($"Failed reading the session file {potentialSessionPath}...retrying after a second");
                        Thread.Sleep(1000);
                        goto retryLabel;
                    }
                    catch (Exception ex)
                    {

                        throw ex;
                    }

                }
            }

            throw new FileNotFoundException(string.Format("Did not find session {0}", sessionId));
        }

        public IEnumerable<Instance> GetAllRunningSiteInstances()
        {
            return HeartBeatController.GetLiveInstances();
        }

        public IEnumerable<Diagnoser> GetAllDiagnosers()
        {
            return Infrastructure.Settings.GetDiagnosers();
        }

        public bool IsSandboxAvailable()
        {
            return Settings.IsSandBoxAvailable();
        }

        public static void RecursiveDelete(string path)
        {
            if (Directory.Exists(path))
            {
                //
                // First delete all files in the Directory
                // 

                string[] files = null;
                try
                {
                    files = Directory.GetFiles(path);
                }
                catch (Exception)
                {
                }
                if (files != null)
                {
                    foreach (string file in files)
                    {
                        try
                        {
                            RetryHelper.RetryOnException("Recursively deleting files...", () =>
                            {
                                System.IO.File.Delete(file);
                            },
                            TimeSpan.FromSeconds(2),
                            times: 5,
                            logAllExceptions: false,
                            throwAfterRetry: false);
                        }
                        catch (Exception)
                        {
                        }

                    }
                }

                //
                // Delete all child Directories
                // 

                string[] directories = null;
                try
                {
                    directories = Directory.GetDirectories(path);
                }
                catch (Exception)
                {
                }

                if (directories != null)
                {
                    foreach (string directory in directories)
                    {
                        RecursiveDelete(directory);
                    }
                }

                try
                {
                    Directory.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }
        private void CleanupEmptyDirectories(DirectoryInfo path)
        {
            if (!Directory.Exists(path.FullName))
            {
                return;
            }

            bool noFailures = true;
            var isEmpty = !Directory.EnumerateFileSystemEntries(path.FullName).Any();
            if (isEmpty)
            {
                var parent = path.Parent;
                try
                {
                    Logger.LogDiagnostic($"Cleaning Empty Directory [{path.FullName} ]");
                    path.Delete();
                }
                catch (Exception)
                {
                    noFailures = false;
                }
                if (noFailures && !parent.Name.Equals("Logs", StringComparison.OrdinalIgnoreCase) && !parent.Name.Equals("Reports", StringComparison.OrdinalIgnoreCase))
                {
                    CleanupEmptyDirectories(parent);
                }
            }
        }

        public void RemoveOlderFilesFromBlob(object state)
        {
            BlobController.RemoveOlderFilesFromBlob();
        }

        public async Task<bool> Delete(Session session, bool deleteActiveSessions = false)
        {
            if (session.Status != SessionStatus.Active || deleteActiveSessions)
            {
                var sessionId = session.SessionId.ToString();
                // For logs, the instance name is stored before the
                // SessionId so we need to enumerate the logs of the
                // session to identify files and folders to delete

                if (string.IsNullOrWhiteSpace(session.BlobSasUri))
                {
                    foreach (var diagnoser in session.GetDiagnoserSessions())
                    {
                        foreach (var instance in diagnoser.GetCollectedInstances())
                        {
                            var log = diagnoser.GetLogs().FirstOrDefault();
                            if (log != null)
                            {
                                if (log.StorageLocation == StorageLocation.UserSiteData)
                                {
                                    var logPath = Path.Combine(
                                                Settings.UserSiteStorageDirectory,
                                               "Logs",
                                               session.DefaultHostName,
                                               session.StartTime.ToString(SessionConstants.SessionFileNameFormat),
                                               instance.Name,
                                               diagnoser.Diagnoser.Collector.Name);

                                    if (Directory.Exists(logPath))
                                    {
                                        var parentLogs = Directory.GetParent(logPath);
                                        RecursiveDelete(logPath);
                                        CleanupEmptyDirectories(parentLogs);
                                    }
                                }
                            }
                        }
                    }

                    var rootPath = Settings.UserSiteStorageDirectory;
                    var reportsPath = Path.Combine(
                              rootPath,
                              "Reports",
                              session.DefaultHostName,
                              session.StartTime.ToString(SessionConstants.SessionFileNameFormat));

                    if (Directory.Exists(reportsPath))
                    {
                        var parentReports = Directory.GetParent(reportsPath);
                        RecursiveDelete(reportsPath);
                        CleanupEmptyDirectories(parentReports);
                    }
                }
                else
                {
                    try
                    {
                        foreach (var diagnoser in session.GetDiagnoserSessions())
                        {
                            foreach (var log in diagnoser.GetLogs())
                            {
                                var leaseFileName = Path.GetDirectoryName(log.RelativePath);
                                await Infrastructure.Storage.DeleteFileAsync(log, session.BlobSasUri);

                                await Infrastructure.Storage.DeleteFileAsync(leaseFileName, session.BlobSasUri);

                                foreach (var report in diagnoser.GetReportsForLog(log))
                                {
                                    await Infrastructure.Storage.DeleteFileAsync(report, session.BlobSasUri);
                                }
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogSessionErrorEvent("Failed while deleting session content from blob", ex, sessionId);
                    }
                }

                RetryHelper.RetryOnException("Deleting session file...", () =>
                {
                    System.IO.File.Delete(session.FullPermanentStoragePath);
                }, TimeSpan.FromSeconds(1), 5, true, false);

                Logger.LogSessionVerboseEvent("Deleted session file and data", sessionId);
                return true;

            }
            else
            {
                return false;
            }
        }

        private Dictionary<SessionId, TaskAndCancellationToken> _runningSessions = new Dictionary<SessionId, TaskAndCancellationToken>();

        public void RunActiveSessions()
        {
            IEnumerable<Session> activeSessions = GetAllActiveSessions();
            foreach (var activeSession in activeSessions)
            {
                if (_runningSessions.ContainsKey(activeSession.SessionId))
                {
                    // This session has already been started
                    continue;
                }

                Logger.LogSessionVerboseEvent($"Starting diagnosers for Session {activeSession.SessionId} on {Environment.MachineName}", activeSession.SessionId.ToString());

                CancellationTokenSource cts = new CancellationTokenSource();
                var sessionTask = RunDiagnosersForSessionAsync(activeSession, cts.Token);

                TaskAndCancellationToken t = new TaskAndCancellationToken
                {
                    UnderlyingTask = sessionTask,
                    CancellationSource = cts
                };
                _runningSessions[activeSession.SessionId] = t;
            }

            // Cleanup completed sessions
            foreach (var sessionId in _runningSessions.Keys.ToList())
            {
                if (_runningSessions[sessionId].UnderlyingTask != null)
                {
                    var status = _runningSessions[sessionId].UnderlyingTask.Status;
                    if (status == TaskStatus.Canceled || status == TaskStatus.Faulted || status == TaskStatus.RanToCompletion)
                    {

                        Logger.LogSessionVerboseEvent($"Task for Session {sessionId} has completed with status {status.ToString()} on {Environment.MachineName}", sessionId.ToString());
                        _runningSessions.Remove(sessionId);
                    }
                }

            }
        }

        private async Task RunDiagnosersForSessionAsync(Session session, CancellationToken ct)
        {
            var diagnoserTasks = new List<Task>();
            foreach (var diagnoserSession in session.GetDiagnoserSessions())
            {
                Task runDiagnoserTask = RunDiagnoserAsync(session, diagnoserSession.Diagnoser.Name, ct);
                diagnoserTasks.Add(runDiagnoserTask);
            }

            // Wait for all the diagnosers to finish running
            await Task.WhenAll(diagnoserTasks.ToArray());

            Logger.LogSessionVerboseEvent(string.Format("All diagnosers for session {0} have run. Session status is {1}", session.SessionId, session.Status), session.SessionId.ToString());
            Logger.LogDiagnostic("Current session relative path is {0}", session.RelativePath);

            session.MoveSessionToCorrectStorageFolderBasedOnStatus();

            Logger.LogDiagnostic("Finished moving session {0}", session.SessionId);
        }

        private async Task RunDiagnoserAsync(Session session, string diagnoserName, CancellationToken ct)
        {
            Logger.LogDiagnostic("Inside RunDiagnoserAsync Method");
            var diagnoserSession = (DiagnoserSession)session.GetDiagnoserSessions().First(d => d.Diagnoser.Name == diagnoserName);

            try
            {
                int loopCount = 0;
                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    Logger.LogDiagnostic("Starting run {0} for diagnoser {1}", ++loopCount, diagnoserSession.Diagnoser.Name);

                    session.LoadLatestUpdates();

                    switch (diagnoserSession.CollectorStatus)
                    {
                        case (DiagnosisStatus.NotRequested):
                        case (DiagnosisStatus.WaitingForInputs):
                            throw new InvalidDataException(
                                string.Format(
                                    "The collector diagnosis status should never be {0}. Have you been tampering with the session files?",
                                    diagnoserSession.CollectorStatus));
                        case (DiagnosisStatus.InProgress):
                            if (!session.ShouldCollectLogsOnInstance(Instance.GetCurrentInstance()))
                            {
                                break;
                            }

                            if (!diagnoserSession.HasThisInstanceRunTheCollector())
                            {
                                diagnoserSession.LogNewCollectorRun();
                                await session.SaveAndMergeUpdatesAsync(waitForLease: true);

                                bool updatedSession = await RunCollectorAsync(session, diagnoserSession.Diagnoser.Name, ct);
                                if (updatedSession)
                                {
                                    await session.SaveAndMergeUpdatesAsync(waitForLease: true);
                                }
                            }

                            bool sessionWasUpdated = MarkCollectorAsCompleteIfAllInstancesHaveFinishedRunning(session,
                                diagnoserSession.Diagnoser.Name);
                            if (sessionWasUpdated)
                            {
                                // Don't block the thread if someone else is modifying the session.
                                // We'll let some other instance update the collector state
                                await session.SaveAndMergeUpdatesAsync(waitForLease: false);
                            }

                            break;

                        case (DiagnosisStatus.Complete):
                            break;

                        case (DiagnosisStatus.Cancelled):
                        case (DiagnosisStatus.Error):
                            return;
                    }

                    switch (diagnoserSession.AnalyzerStatus)
                    {
                        case (DiagnosisStatus.InProgress):

                            // Make sure none of logs collected are getting analyzed for
                            // more than 20 minutes, if they are, lets cancel the session

                            if (diagnoserSession.IsAnalyzerRunningForLongTime())
                            {
                                var exMessage = GetAnalyzerTimeoutMessage(diagnoserSession, session.SessionId.ToString());
                                DiagnosticToolHasNoOutputException ex = new DiagnosticToolHasNoOutputException(diagnoserSession.Diagnoser.Analyzer.Name, exMessage);
                                diagnoserSession.LogAnalyzerFailure(ex);
                                diagnoserSession.AnalyzerStatus = DiagnosisStatus.Cancelled;
                                session.SaveAndMergeUpdatesAsync(waitForLease: false).Wait();
                                await session.CancelSessionAsync();
                                return;
                            }

                            Logger.LogSessionVerboseEvent($"Analyzer Status is {diagnoserSession.AnalyzerStatus} and there are {diagnoserSession.GetUnanalyzedLogs().Count()} Unanalyzed logs left", session.SessionId.ToString());
                            if (diagnoserSession.GetUnanalyzedLogs().Count() > 0)
                            {
                                Logger.LogDiagnostic("Starting analyzer call");
                                diagnoserSession.LogNewAnalyzerRun();
                                await session.SaveAndMergeUpdatesAsync(waitForLease: true);
                                await RunAnalyzerAsync(session, diagnoserSession.Diagnoser.Name, ct);
                                Logger.LogDiagnostic("Finished analyzer call");
                            }
                            else
                            {
                                // We will come in this loop if some other  instance is analyzing the log too, we will
                                // just mark session has Complete if there are no logs left to analyze. I am not sure
                                // if this is really necessary but lets see if this gets logged by any chance

                                if (!diagnoserSession.HasUnanalyzedLogs())
                                {
                                    Logger.LogSessionVerboseEvent($"Going to mark Diagnoser Session for session {session.SessionId} to complete because session has no logs left to analyze", session.SessionId.ToString());
                                    diagnoserSession.AnalyzerStatus = DiagnosisStatus.Complete;
                                    session.SaveAndMergeUpdatesAsync(waitForLease: false).Wait();
                                    Logger.LogSessionVerboseEvent($"Diagnoser session marked complete for session {session.SessionId} on this instance and session file updated successfully", session.SessionId.ToString());
                                }
                            }
                            break;

                        case (DiagnosisStatus.WaitingForInputs):
                            Logger.LogDiagnostic("Analyzer is waiting for collectors to finish running");
                            if (diagnoserSession.CollectorStatus == DiagnosisStatus.Complete)
                            {
                                Logger.LogDiagnostic("Analyzer: Hey, turns out all the collectors were done anyways");
                                diagnoserSession.AnalyzerStatus = DiagnosisStatus.InProgress;
                                diagnoserSession.AnalyzerStartTime = DateTime.UtcNow;
                                await session.SaveAndMergeUpdatesAsync();
                            }
                            break;

                        case (DiagnosisStatus.NotRequested):
                            if (diagnoserSession.CollectorStatus == DiagnosisStatus.Complete)
                            {
                                return;
                            }
                            break;

                        case (DiagnosisStatus.Cancelled):
                        case (DiagnosisStatus.Complete):
                        case (DiagnosisStatus.Error):
                            Logger.LogDiagnostic("Analyzer status for {0} is: {1}. Ending session", diagnoserSession.Diagnoser.Name, diagnoserSession.AnalyzerStatus);
                            return;
                    }

                    // This sleep timer will be hit when the collector on the current instance has finished running
                    //   or does not have to collect this instance, but other instances have not finished collecting yet.
                    Logger.LogDiagnostic("Sleeping for a bit, then I'll try again");
                    await Task.Delay(Infrastructure.Settings.FrequencyToCheckForNewSessionsAt);
                    Logger.LogDiagnostic("Waking up. Is it spring already?");

                    int sessionTotalSeconds = Convert.ToInt32(DateTime.UtcNow.Subtract(session.StartTime).TotalSeconds);
                    if (sessionTotalSeconds > 900)
                    {
                        int interval = sessionTotalSeconds / 900;
                        int currentInterval = 900 * interval;

                        if (sessionTotalSeconds > currentInterval && sessionTotalSeconds < currentInterval + 15)
                        {
                            Logger.LogSessionVerboseEvent($"Found long running Session - {session.SessionId} running for {sessionTotalSeconds / 60} minutes", session.SessionId.ToString());
                        }
                    }

                    if (sessionTotalSeconds > (Infrastructure.Settings.MaxSessionTimeInMinutes * 60))
                    {
                        await session.CancelSessionAsync();
                        Logger.LogSessionVerboseEvent($"Cancelling session {session.SessionId} with total duration { sessionTotalSeconds / 60 } minutes as the total duration crossed an hour", session.SessionId.ToString());
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Exception in RunDiagnoserAsync Method", ex, session.SessionId.ToString());
            }
        }

        internal async Task<bool> RunCollectorAsync(Session session, string diagnoserName, CancellationToken ct)
        {
            bool updatedSession = false;

            var diagnoserSession = (DiagnoserSession)session.GetDiagnoserSessions().First(d => d.Diagnoser.Name == diagnoserName);
            Diagnoser diagnoser = diagnoserSession.Diagnoser;

            if (!diagnoserSession.IsCollectorHealthy())
            {
                diagnoserSession.CollectorStatus = DiagnosisStatus.Error;
                updatedSession = true;
                return updatedSession;
            }

            try
            {
                Logger.LogDiagnostic("Running collector {0} for session {1} with StartTime {2}", diagnoser.Collector.Name, session.SessionId, session.StartTime.ToString());
                var logs = (await diagnoser.CollectLogs(session.StartTime, session.EndTime, session.SessionId.ToString(), session.BlobSasUri, session.DefaultHostName, ct));
                if (logs == null || !logs.Any())
                {
                    Logger.LogDiagnostic("We were not able to run the collector (maybe some another instance is already running it or it's not yet time to run the collector)");
                    // Were not able to run the collector (perhaps because another instance is already running it)
                    return updatedSession;
                }

                updatedSession = true;

                foreach (var log in logs)
                {
                    Logger.LogDiagnostic("Adding log " + log.RelativePath);
                    diagnoserSession.AddLog(log);
                }

                return updatedSession;
            }
            catch (AggregateException ae)
            {
                foreach (var e in ae.InnerExceptions)
                {
                    if (e is DiagnosticToolErrorException)
                    {
                        diagnoserSession.LogCollectorFailure(e);
                        Logger.LogSessionErrorEvent(string.Format("Collector {0} did not run successfully.", diagnoser.Collector.Name), e, session.SessionId.ToString());
                    }
                    else if (e is DiagnosticToolHasNoOutputException)
                    {
                        diagnoserSession.LogCollectorFailure(e);
                        Logger.LogSessionVerboseEvent(string.Format("Collector {0} did not generate any logs", diagnoser.Collector.Name), session.SessionId.ToString());
                    }
                    else
                    {
                        diagnoserSession.LogCollectorFailure(e);
                        Logger.LogSessionErrorEvent($"Unexpected error occurred when running Collector:{diagnoser.Collector.Name}", e, session.SessionId.ToString());
                    }
                }
            }
            catch (DiagnosticToolErrorException e)
            {
                diagnoserSession.LogCollectorFailure(e);
                Logger.LogSessionErrorEvent($"Collector {diagnoser.Collector.Name} did not run successfully.", e, session.SessionId.ToString());
            }
            catch (DiagnosticToolHasNoOutputException e)
            {
                diagnoserSession.LogCollectorFailure(e);
                Logger.LogSessionErrorEvent($"Collector {diagnoser.Collector.Name} did not generate any logs", e, session.SessionId.ToString());
            }
            catch (Exception e)
            {
                diagnoserSession.LogCollectorFailure(e);
                Logger.LogSessionErrorEvent($"Unexpected error when running Collector: {diagnoser.Collector.Name}", e, session.SessionId.ToString());
            }

            Logger.LogSessionVerboseEvent($"Failed to run collector {diagnoser.Collector.Name}", session.SessionId.ToString());
            updatedSession = true;
            return updatedSession;
        }

        private static bool MarkCollectorAsCompleteIfAllInstancesHaveFinishedRunning(Session session, string diagnoserName)
        {
            var diagnoserSession = (DiagnoserSession)session.GetDiagnoserSessions().First(d => d.Diagnoser.Name == diagnoserName);
            if (!diagnoserSession.AllRequiredInstancesHaveRunTheCollector(session.InstancesSpecified))
            {
                // Wait for all instances to finish collecing their logs
                return false;
            }
            Logger.LogDiagnostic("Updating collector as completed");
            diagnoserSession.CollectorStatus = DiagnosisStatus.Complete;
            Logger.LogDiagnostic("Starting Analyzer");
            if (diagnoserSession.AnalyzerStatus == DiagnosisStatus.WaitingForInputs)
            {
                diagnoserSession.AnalyzerStatus = DiagnosisStatus.InProgress;
                diagnoserSession.AnalyzerStartTime = DateTime.UtcNow;
            }
            Logger.LogSessionVerboseEvent($"Marking Collector as Complete for Session {session.SessionId} and going to start Analyzer", session.SessionId.ToString());
            return true;
        }

        private async Task RunAnalyzerAsync(Session session, string diagnoserName, CancellationToken ct)
        {
            var diagnoserSession = (DiagnoserSession)session.GetDiagnoserSessions().First(d => d.Diagnoser.Name == diagnoserName);
            Diagnoser diagnoser = diagnoserSession.Diagnoser;

            Logger.LogDiagnostic("Checking if analyzer is healthy");
            if (!diagnoserSession.IsAnalyzerHealthy())
            {
                Logger.LogDiagnostic("It aint healthy");
                diagnoserSession.AnalyzerStatus = DiagnosisStatus.Error;
                session.SaveAndMergeUpdatesAsync().Wait();
                return;
            }

            Logger.LogDiagnostic("It's healthy as a Kryptonian on earth");

            try
            {
                var logsRequiringAnalysis = diagnoserSession.GetUnanalyzedLogs();
                Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} has {logsRequiringAnalysis.Count()} remaining logs to analyze", session.SessionId.ToString());

                foreach (var log in logsRequiringAnalysis)
                {
                    List<Report> reports = null;
                    Logger.LogSessionVerboseEvent($"Running analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} for log {log.FileName}", session.SessionId.ToString());

                    // Lets just make sure that another instance didn't end up analyzing this log
                    // while we were looping through the logs;
                    var updatedDiagnoserSession = (DiagnoserSession)session.GetDiagnoserSessions().First(d => d.Diagnoser.Name == diagnoserName);
                    if (updatedDiagnoserSession.GetUnanalyzedLogs().Contains(log))
                    {
                        log.AnalysisStarted = DateTime.UtcNow;
                        log.InstanceAnalyzing = Environment.MachineName;

                        // Since we updated AnalysisStarted, we shoud save that to XML
                        session.SaveAndMergeUpdatesAsync(waitForLease: true).Wait();

                        var analyzerStartTime = DateTime.UtcNow;
                        Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} going to analyze log {log.FileName}", session.SessionId.ToString());

                        try
                        {
                            reports = await diagnoser.Analyze(log, session.SessionId.ToString(), session.BlobSasUri, session.DefaultHostName, ct);
                        }
                        catch (OperationCanceledException)
                        {
                            var exMessage = GetAnalyzerTimeoutMessage(diagnoserSession, session.SessionId.ToString());
                            DiagnosticToolHasNoOutputException ex = new DiagnosticToolHasNoOutputException(diagnoser.Analyzer.Name, exMessage);
                            diagnoserSession.LogAnalyzerFailure(ex);
                            Logger.LogSessionVerboseEvent($"Diagnoser session marked Cancelled for session {session.SessionId} on this instance ", session.SessionId.ToString());
                            diagnoserSession.AnalyzerStatus = DiagnosisStatus.Cancelled;
                            session.SaveAndMergeUpdatesAsync(waitForLease: false).Wait();
                            await session.CancelSessionAsync();
                            return;
                        }
                        catch (Exception ex)
                        {
                            // Set the AnalysisStarted bit so that Analyzer
                            // can retry analyzing the log file
                            log.AnalysisStarted = DateTime.MinValue.ToUniversalTime();
                            throw ex;
                        }

                        Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} finished analyzing log {log.FileName} in {DateTime.UtcNow.Subtract(analyzerStartTime).TotalMinutes} minutes. Reports is NULL = {reports == null}", session.SessionId.ToString());

                        // null reports means some other instance is running the analyzer
                        if (reports != null)
                        {
                            diagnoserSession.AddReportsForLog(log, reports.ToList());
                        }

                        Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} is going to update the session", session.SessionId.ToString());

                        session.SaveAndMergeUpdatesAsync(waitForLease: true).Wait();

                        Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} updated the session successfully", session.SessionId.ToString());

                        // Reload diagnoser session in case we've gotten an updated session state
                        diagnoserSession =
                            (DiagnoserSession)session.GetDiagnoserSessions().First(d => d.Diagnoser.Name == diagnoserName);
                    }
                    else
                    {
                        Logger.LogSessionVerboseEvent($"{log.FileName} is already analyzed by another instance", session.SessionId.ToString());
                    }
                }

                if (!diagnoserSession.HasUnanalyzedLogs())
                {
                    Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} finished on this instance, updated session file", session.SessionId.ToString());
                    diagnoserSession.AnalyzerStatus = DiagnosisStatus.Complete;
                    session.SaveAndMergeUpdatesAsync(waitForLease: false).Wait();
                    Logger.LogSessionVerboseEvent($"Analyzer {diagnoser.Analyzer.Name} for session {session.SessionId} finished on this instance and session file updated successfully", session.SessionId.ToString());
                }

                Logger.LogDiagnostic("No errors encountered in analyzer");
                return;
            }
            catch (AggregateException ae)
            {
                foreach (var e in ae.InnerExceptions)
                {
                    if (e is DiagnosticToolErrorException)
                    {
                        diagnoserSession.LogAnalyzerFailure(e);
                        Logger.LogSessionErrorEvent(string.Format("Analyzer {0} did not run successfully.", diagnoser.Analyzer.Name), e, session.SessionId.ToString());
                    }
                    else if (e is DiagnosticToolHasNoOutputException)
                    {
                        diagnoserSession.LogAnalyzerFailure(e);
                        Logger.LogSessionErrorEvent(string.Format("Analyzer {0} did not generate any reports.", diagnoser.Analyzer.Name), e, session.SessionId.ToString());
                    }
                    else
                    {
                        diagnoserSession.LogAnalyzerFailure(e);
                        Logger.LogSessionErrorEvent(string.Format("Unexpected error occurred when running Analyzer: {0}", diagnoser.Analyzer.Name), e, session.SessionId.ToString());
                    }
                }
            }
            catch (DiagnosticToolErrorException e)
            {
                diagnoserSession.LogAnalyzerFailure(e);
                Logger.LogSessionErrorEvent(string.Format("Analyzer {0} did not run successfully.", diagnoser.Analyzer.Name), e, session.SessionId.ToString());
            }
            catch (DiagnosticToolHasNoOutputException e)
            {
                diagnoserSession.LogAnalyzerFailure(e);
                Logger.LogSessionErrorEvent(string.Format("Analyzer {0} did not generate any reports.", diagnoser.Analyzer.Name), e, session.SessionId.ToString());
            }
            catch (Exception e)
            {
                diagnoserSession.LogAnalyzerFailure(e);
                Logger.LogSessionErrorEvent(string.Format("Unexpected error occurred when running Analyzer: {0}", diagnoser.Analyzer.Name), e, session.SessionId.ToString());

            }

            session.SaveAndMergeUpdatesAsync(waitForLease: false).Wait();
        }

        private string GetAnalyzerTimeoutMessage(DiagnoserSession diagnoserSession, string sessionId)
        {
            var exceptionMessage = "Analyzer timed out or was cancelled while analyzing the log. ";
            try
            {
                double analyzerRunningTime = 0;
                if (diagnoserSession.AnalyzerStartTime != DateTime.MinValue.ToUniversalTime())
                {
                    analyzerRunningTime = DateTime.UtcNow.Subtract(diagnoserSession.AnalyzerStartTime).TotalMinutes;
                }

                if (analyzerRunningTime > 1)
                {
                    exceptionMessage += $"Total time analyzer ran is {analyzerRunningTime.ToString("0.0")} minutes";
                }
                Logger.LogSessionVerboseEvent($"Analyzer {diagnoserSession.Diagnoser.Analyzer.Name} for session {sessionId} ran for > {Infrastructure.Settings.MaxAnalyzerTimeInMinutes} minutes so cancelling the session. Message send to user {exceptionMessage}", sessionId);
            }
            catch (Exception)
            {
            }
            return exceptionMessage;
        }

        public void StartSessionRunner(string sourceDir = null, List<string> extraFilesToCopy = null)
        {
            var daasDisabled = Environment.GetEnvironmentVariable("WEBSITE_DAAS_DISABLED");
            if (daasDisabled != null && daasDisabled.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                DeleteWebjobFolderIfExists(EnvironmentVariables.DaasWebJobAppData);
                DeleteWebjobFolderIfExists(EnvironmentVariables.DaasWebJob);
            }
            else
            {
                if (_daasVersionCheckInProgress == false)
                {
                    lock (_daasVersionUpdateLock)
                    {
                        _daasVersionCheckInProgress = true;
                    }

                    try
                    {
                        Logger.LogVerboseEvent("Checking DaaS bits and updating if required");
                        const string daaSRunner = "DaaSRunner.exe";
                        const string daaSConsole = "DaaSConsole.exe";
                        const string daaSDll = "DaaS.dll";
                        const string newtonsoft = "Newtonsoft.Json.dll";
                        const string microsoftWindowsAzureStorage = "Microsoft.WindowsAzure.Storage.dll";
                        const string systemIoAbstractions = "System.IO.Abstractions.dll";

                        var filesToCopy = new List<string>() { daaSRunner, daaSDll, microsoftWindowsAzureStorage, newtonsoft, systemIoAbstractions };

                        if (extraFilesToCopy != null)
                        {
                            filesToCopy.AddRange(extraFilesToCopy);
                        }

                        if (sourceDir == null)
                        {
                            sourceDir = Path.Combine(Infrastructure.GetDaasInstalationPath(), "bin");
                        }

                        var destinationDir = @"site\jobs\Continuous\DaaS";
                        var continousJobDirectory = Path.Combine(Settings.SiteRootDir, destinationDir);


                        List<string> existingFiles = new List<string>();
                        if (Directory.Exists(continousJobDirectory))
                        {
                            foreach (var fileName in Directory.EnumerateFiles(continousJobDirectory))
                            {
                                string filenameWithoutPath = Path.GetFileName(fileName);
                                existingFiles.Add(filenameWithoutPath);
                            }
                        }
                        CreateDirectoryIfNotExists(continousJobDirectory);
                        var daasBinDirectory = Path.Combine(Settings.SiteRootDir, @"data\DaaS\bin");
                        CreateDirectoryIfNotExists(daasBinDirectory);

                        filesToCopy.Sort();
                        existingFiles.Sort();

                        bool filesMatching = true;
                        foreach (var item in filesToCopy)
                        {
                            if (!existingFiles.Contains(item, StringComparer.OrdinalIgnoreCase))
                            {
                                filesMatching = false;
                                break;
                            }
                        }

                        bool versionsDifferent = !IsVersionMatch(Path.Combine(sourceDir, daaSRunner), destinationDir);

                        if (versionsDifferent || filesMatching == false)
                        {
                            Logger.LogVerboseEvent($"Going to update DaaS bits because versionsDifferent = {versionsDifferent} and filesMatching = {filesMatching}");

                            if (DeleteWebjobFolderIfExists(EnvironmentVariables.DaasWebJobAppData))
                            {
                                Logger.LogVerboseEvent(@"Deleted daas webjob from App_Data and moving to site\jobs\Continous folder and waiting for 30 seconds before adding the new webjob");
                                Thread.Sleep(30000);
                            }

                            try
                            {
                                foreach (var file in filesToCopy)
                                {
                                    Logger.LogVerboseEvent($"[DAAS Webjob] Copying file {file} from {sourceDir} to {destinationDir}");
                                    CopyFile(file, sourceDir, destinationDir);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogErrorEvent("Failed while updating DaasRunner in the jobs folder", ex);
                            }

                            try
                            {
                                // Also copy DaaSConsole to a fixed location for auto healing invocations
                                filesToCopy.Add(daaSConsole);
                                filesToCopy.Remove(daaSRunner);
                                destinationDir = @"data\DaaS\bin";
                                foreach (var file in filesToCopy)
                                {
                                    Logger.LogVerboseEvent($"[DAAS bin folder] Copying file {file} from {sourceDir} to {destinationDir}");
                                    CopyFile(file, sourceDir, destinationDir);
                                }
                                Logger.LogVerboseEvent("Updated DaaS webjob files with newer versions");
                            }
                            catch (Exception ex)
                            {
                                Logger.LogErrorEvent(@"Failed while copying DAAS bits to \home\data\daas\bin directory", ex);
                            }
                        }

                        try
                        {
                            ValidateAllDllVersions(sourceDir, filesToCopy.Where(x => x.ToLower().EndsWith(".dll")).ToList()); ;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogErrorEvent("Failed while validating daas versions in Jobs and Bin folder", ex);
                        }

                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorEvent("Failed while checking or updating DaaSRunner", ex);
                    }

                    lock (_daasVersionUpdateLock)
                    {
                        _daasVersionCheckInProgress = false;
                    }

                    Logger.LogVerboseEvent("Done checking DaaS bits for any new updates");
                }
                else
                {
                    Logger.LogVerboseEvent("Another check to update DaaS bits is in progress");
                    // there is already another thread updating DaasRunner
                    // so don't do anything right now
                }
            }
        }

        private void ValidateAllDllVersions(string sourceDir, List<string> filesToCopy)
        {
            string destinationDir = @"site\jobs\Continuous\DaaS";
            var outdatedFiles = CheckFileVersionInFolder(sourceDir, destinationDir, filesToCopy);
            foreach (var file in outdatedFiles)
            {
                Logger.LogVerboseEvent($"Version mismatch found or file not found so copying {file} from {sourceDir} to {destinationDir}");
                CopyFile(file, sourceDir, destinationDir);
            }

            destinationDir = @"data\DaaS\bin";
            outdatedFiles = CheckFileVersionInFolder(sourceDir, destinationDir, filesToCopy);
            foreach (var file in outdatedFiles)
            {
                Logger.LogVerboseEvent($"Version mismatch found or file not found so copying {file} from {sourceDir} to {destinationDir}");
                CopyFile(file, sourceDir, destinationDir);
            }
        }

        private List<string> CheckFileVersionInFolder(string sourceDir, string destinationDir, List<string> filesToCopy)
        {
            var dllsNeedingUpdate = new List<string>();
            StringBuilder logStatement = new StringBuilder();
            destinationDir = Path.Combine(Settings.SiteRootDir, destinationDir);
            foreach (var file in filesToCopy)
            {
                var sourceFile = Path.Combine(sourceDir, file);
                var destinationFile = Path.Combine(destinationDir, file);

                // not sure if this can happen but lets be safe...
                if (!System.IO.File.Exists(sourceFile))
                {
                    logStatement.Append($" Sourcefile {sourceFile} does not exist!");
                    continue;
                }

                if (System.IO.File.Exists(destinationFile))
                {
                    Version sourceVersion = GetFileVersion(sourceFile);
                    Version destinationVersion = GetFileVersion(destinationFile);

                    if (sourceVersion != destinationVersion)
                    {
                        logStatement.Append($" Version mismatch found for {file}");
                        dllsNeedingUpdate.Add(file);
                    }
                }
                else
                {
                    logStatement.Append($" [{destinationFile}] does not exist in [{destinationDir}],");
                    dllsNeedingUpdate.Add(file);

                }
            }

            var logMsg = string.IsNullOrWhiteSpace(logStatement.ToString()) ? "All Good" : logStatement.ToString();

            Logger.LogVerboseEvent($"Checking all file versions in {destinationDir} :{logMsg}");
            return dllsNeedingUpdate;
        }

        private bool DeleteWebjobFolderIfExists(string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath))
                {
                    RetryHelper.RetryOnException("Deleting webjob from AppData if exists...", () =>
                    {
                        System.IO.File.Delete(file);
                    }, TimeSpan.FromSeconds(1));
                }

                try
                {
                    Directory.Delete(fullPath);
                }
                catch (Exception)
                {
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool IsVersionMatch(string daasRunnerPath, string destinationDir)
        {
            var currentVersion = GetDaasRunnerVersion();

            if (currentVersion != null)
            {
                if (!System.IO.File.Exists(daasRunnerPath))
                {
                    Logger.LogVerboseEvent($"Found no DaasRunner in {daasRunnerPath}");
                    return false;
                }

                Version packageVersion = GetFileVersion(daasRunnerPath);

                //we don't match exactly if version is greater/lower - we need to copy whatever is in Daas package
                if (packageVersion.CompareTo(currentVersion) != 0)
                {
                    // If we didn't find DAASRunner.exe running, then we should check if the job folder
                    // already had a version of DAAS that matches ProgramFiles(x86)\SiteExtensions folder
                    // to avoid changing the job directory
                    if (currentVersion.Major == 0)
                    {
                        var daasRunnerInJobDirectory = Path.Combine(Settings.SiteRootDir, destinationDir, "DaasRunner.exe");
                        Logger.LogVerboseEvent($"DaasRunner process not running so checking DaasRunner.exe version in {daasRunnerInJobDirectory}");

                        Version jobAssemblyVersion = null;

                        if (System.IO.File.Exists(daasRunnerInJobDirectory))
                        {
                            jobAssemblyVersion = GetFileVersion(daasRunnerInJobDirectory);
                        }

                        if (jobAssemblyVersion != null && packageVersion.CompareTo(jobAssemblyVersion) == 0)
                        {
                            Logger.LogVerboseEvent(string.Format("Current version in jobs Directory : {0} matches the version in Daas installation path : {1} so no need to copy new files", jobAssemblyVersion, packageVersion));
                            return true;
                        }
                        else
                        {
                            Logger.LogVerboseEvent(string.Format("Current version in Jobs Folder : {0} does not match version in Daas installation path : {1}, new bits will be copied", jobAssemblyVersion == null ? "0.0.0.0" : jobAssemblyVersion.ToString(), packageVersion));
                            return false;
                        }
                    }
                    else
                    {
                        Logger.LogVerboseEvent(string.Format("Current version : {0} does not match version in Daas installation path : {1}, new bits will be copied", currentVersion, packageVersion));
                        return false;
                    }
                }
                else
                {
                    Logger.LogVerboseEvent("DaasRunner version is the same");
                }
            }

            // if we are not able to fetch DaasRunner version then we also don't copy anything for web job
            return true;
        }

        private static Version GetDaasRunnerVersion()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("DaaSRunner");

                if (processes.Length > 0)
                {
                    string path = processes.FirstOrDefault().GetMainModuleFileName();
                    var daasRunnerVersion = GetFileVersion(path);
                    Logger.LogVerboseEvent($"Found DaasRunner process running in {path} with version {daasRunnerVersion}");
                    return daasRunnerVersion;
                }
                else
                {
                    Logger.LogVerboseEvent($"DaasRunner process not running");
                    return new Version(0, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Error occured while getting DaasRunner version", ex);
            }

            return null;
        }

        private static void CopyFile(string file, string sourceDir, string destinationDir)
        {
            // Changing this method to ensure that it doesn't have any dependency
            // on any of the DAAS classes else we will get AssemblyLoad exceptions

            var siteRootDir = Environment.GetEnvironmentVariable("HOME_EXPANDED");
            var targetFile = Path.Combine(siteRootDir, destinationDir, file);
            var sourceFile = Path.Combine(sourceDir, file);
            RetryHelper.RetryOnException($"Copying file from {sourceFile} to {targetFile}...", () =>
            {
                System.IO.File.Copy(sourceFile, targetFile, true);
            }, TimeSpan.FromSeconds(1));

        }

        private static void CreateDirectoryIfNotExists(string folder)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        private static Version GetFileVersion(string filePath)
        {
            Version ver = new Version(0, 0, 0, 0);
            var fileVersion = FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            try
            {
                ver = Version.Parse(fileVersion);
            }
            catch (Exception)
            {
            }
            return ver;
        }

    }


}
