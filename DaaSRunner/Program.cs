// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using DaaS;
using DaaS.Sessions;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using DaaS.Configuration;
using DaaS.Storage;

namespace DaaSRunner
{
    class Program
    {
        enum Verbosity
        {
            Information,
            Diagnostic
        }

        private static bool m_MonitoringEnabled = false;
        private static readonly CpuMonitoring m_CpuMonitoring = new CpuMonitoring();
        private static ICpuMonitoringRule m_CpuMonitoringRule = null;
        private static bool m_FailedStoppingSession = false;
        private static Timer m_SasUriTimer;
        private static Timer m_CompletedSessionsCleanupTimer;

        private static DateTime _lastSessionCleanupTime = DateTime.UtcNow;
        private static DateTime _lastV2SessionUpdatedTime = DateTime.UtcNow;

        private static readonly ISessionManager _sessionManager = new SessionManager(new AzureStorageService());
        private static readonly IAzureStorageSessionManager _azureStorageSessionManager = new AzureStorageSessionManager(new AzureStorageService());
        private static readonly ConcurrentDictionary<string, TaskAndCancellationToken> _runningSessions = new ConcurrentDictionary<string, TaskAndCancellationToken>();
        private static readonly ConcurrentDictionary<string, TaskAndCancellationToken> _runningV2Sessions = new ConcurrentDictionary<string, TaskAndCancellationToken>();

        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private static readonly IStorageService storageService = new AzureStorageService();

        static void Main(string[] args)
        {
            Logger.LogVerboseEvent($"DaasRunner.exe with version {Assembly.GetExecutingAssembly().GetName().Version} and ProcessId={Process.GetCurrentProcess().Id} started");

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            SessionController sessionController = new SessionController();
            sessionController.StartSessionRunner();

            if (AppDomain.CurrentDomain.IsDefaultAppDomain())
            {
                AppDomain.CurrentDomain.ProcessExit += DaasRunnerTerminationHandler;
                AppDomain.CurrentDomain.UnhandledException += DaasRunner_UnhandledException;
            }
            else
            {
                AppDomain.CurrentDomain.DomainUnload += DaasRunnerTerminationHandler;
                AppDomain.CurrentDomain.UnhandledException += DaasRunner_UnhandledException;
            }

            // Try to stagger the start times of all the instances. It'll help avoid file contention
            Thread.Sleep(new Random().Next(10));

            //StartSendingHeartBeats();
            Thread.Sleep(TimeSpan.FromSeconds(20));

            // Start a timer to validate SAS URI's are configured correctly
            m_SasUriTimer = new Timer(new TimerCallback(ValidateStorageConfiguration), null, (int)TimeSpan.FromMinutes(1).TotalMilliseconds, (int)TimeSpan.FromHours(4).TotalMilliseconds);

            // Start a timer to cleanup completed sessions
            m_CompletedSessionsCleanupTimer = new Timer(new TimerCallback(CompletedSessionCleanup), null, (int)TimeSpan.FromMinutes(1).TotalMilliseconds, (int)TimeSpan.FromHours(4).TotalMilliseconds);

            InitializeThreadsForCpuMonitoring();

            StartSessionRunner();
        }

        private static void ValidateStorageConfiguration(object state)
        {
            if (!Settings.Instance.IsStorageAccountConfigured)
            {
                Logger.LogVerboseEvent("Storage account is not configured");
                return;
            }

            try
            {
                var isValid = storageService.ValidateStorageConfiguration(out string _, out Exception exceptionContactingStorage);
                if (!isValid)
                {
                    if (exceptionContactingStorage != null)
                    {
                        Logger.LogErrorEvent("Invalid storage configuration", exceptionContactingStorage);
                    }
                    else
                    {
                        Logger.LogErrorEvent("Invalid storage configuration", "Storage configuration is invalid");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Encountered exception while validating storage configuration", ex);
            }
        }

        private static void CheckAnalysisForCpuMonitoring()
        {
            while (true)
            {
                MonitoringAnalysisController.DequeueAnalysisRequest();
                MonitoringAnalysisController.ReSubmitExpiredRequests();
                Thread.Sleep(30 * 1000);
            }
        }

        private static void CompletedSessionCleanup(object state)
        {
            int maxSessionsToKeep = Settings.Instance.MaxSessionsToKeep;
            int numberOfDays = Settings.Instance.MaxSessionAgeInDays;
            MonitoringSessionController controllerMonitoring = new MonitoringSessionController();
            var completedMonitoringSessions = controllerMonitoring.GetAllCompletedSessions().OrderByDescending(s => s.StartDate).ToList();

            Logger.LogVerboseEvent($"Starting cleanup for Completed Sessions MaxDiagnosticSessionsToKeep = [{maxSessionsToKeep}] MaxNumberOfDaysForSessions= [{numberOfDays}]");

            List<MonitoringSession> monitoringSessionsToRemove = completedMonitoringSessions.Skip(maxSessionsToKeep).ToList();
            string logMessage = $"[MaxDiagnosticSessionsToKeep] Found {monitoringSessionsToRemove.Count()} monitoring sessions to remove as we have {completedMonitoringSessions.Count()} completed sessions";
            DeleteSessions(monitoringSessionsToRemove, (session) => { controllerMonitoring.DeleteSessionAsync(session.SessionId).Wait(); }, logMessage);

            completedMonitoringSessions = controllerMonitoring.GetAllCompletedSessions().OrderByDescending(s => s.StartDate).ToList();
            List<MonitoringSession> olderSessionsMonitoring = completedMonitoringSessions.Where(x => x.StartDate < DateTime.UtcNow.AddDays(-1 * numberOfDays)).ToList();
            logMessage = $"[MaxNumberOfDaysForSessions] Found {olderSessionsMonitoring.Count()} older monitoring sessions to remove";
            DeleteSessions(olderSessionsMonitoring, (session) => { controllerMonitoring.DeleteSessionAsync(session.SessionId).Wait(); }, logMessage);

        }

        private static bool CheckIfTimeToCleanupSymbols()
        {
            try
            {
                var activeSession = _sessionManager.GetActiveSessionAsync().Result;
                if (activeSession != null)
                {
                    return false;
                }

                var completedSessions = _sessionManager.GetCompletedSessionsAsync().Result;
                var cleanupSymbols = false;
                var lastCompletedSession = completedSessions.FirstOrDefault();

                if (lastCompletedSession != null)
                {
                    if (DateTime.UtcNow.Subtract(lastCompletedSession.StartTime).TotalDays > 1)
                    {
                        cleanupSymbols = true;
                    }
                }

                return cleanupSymbols;
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Unhandled exception while cleanup up ", ex);
            }

            return false;
        }

        private static void CleanupSymbolsDirectory()
        {
            try
            {
                Directory.Delete(DaaS.EnvironmentVariables.DaasSymbolsPath, true);
            }
            catch (Exception)
            {
            }
        }

        private static void DeleteSessions<T>(List<T> sessionsToRemove, Action<T> deleteAction, string logMessage = "")
        {
            if (sessionsToRemove.Count() > 0)
            {
                foreach (var session in sessionsToRemove)
                {
                    try
                    {
                        deleteAction.Invoke(session);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorEvent($"Failed while deleting DAAS Session for {logMessage}", ex);
                    }
                }
                if (!string.IsNullOrWhiteSpace(logMessage))
                {
                    Logger.LogVerboseEvent(logMessage);
                }

                Thread.Sleep(60 * 1000);
            }

        }

        private static void InitializeThreadsForCpuMonitoring()
        {
            ThreadStart tsActualMonitoring = new ThreadStart(ActualMonitoringTask);
            Thread tActualMonitoringThread = new Thread(tsActualMonitoring);
            tActualMonitoringThread.Start();

            ThreadStart tsMonitoringAnalysis = new ThreadStart(CheckAnalysisForCpuMonitoring);
            Thread tMonitoringAnalysis = new Thread(tsMonitoringAnalysis);
            tMonitoringAnalysis.Start();
        }

        private static void ActualMonitoringTask()
        {
            while (true)
            {
                try
                {
                    // Perform this check once more to ensure we have the latest session information
                    if (CheckMonitoringEnabled())
                    {
                        if (!m_MonitoringEnabled)
                        {
                            m_MonitoringEnabled = true;
                            m_CpuMonitoring.InitializeMonitoring(m_CpuMonitoringRule);
                        }

                        if (m_MonitoringEnabled && m_CpuMonitoringRule != null)
                        {
                            if (!m_FailedStoppingSession)
                            {
                                bool shouldExit = m_CpuMonitoring.MonitorCpu(m_CpuMonitoringRule);
                                if (shouldExit)
                                {
                                    StopMonitoringSession();
                                }
                            }
                            else
                            {
                                StopMonitoringSession();
                            }
                        }
                    }
                    else
                    {
                        m_MonitoringEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDiagnostic($"Exception in actual monitoring task : {ex.ToLogString()}");
                }

                if (m_MonitoringEnabled && m_CpuMonitoringRule != null)
                {
                    Logger.LogDiagnostic($"Monitoring is enabled, sleeping for {m_CpuMonitoringRule.MonitorDuration * 1000} milliseconds");
                    Thread.Sleep(m_CpuMonitoringRule.MonitorDuration * 1000);
                }
                else
                {
                    Logger.LogDiagnostic($"Monitoring is not enabled, sleeping for 60 seconds and will check again");
                    Thread.Sleep(60 * 1000);
                }
            }
        }

        private static void StopMonitoringSession()
        {
            bool analysisNeeded = false;
            string sessionId = "";
            if (m_CpuMonitoringRule != null)
            {
                sessionId = m_CpuMonitoringRule.SessionId;
                analysisNeeded = m_CpuMonitoringRule.ShouldAnalyze();
            }
            Logger.LogCpuMonitoringVerboseEvent($"Stopping a monitoring session", sessionId);

            MonitoringSessionController sessionController = new MonitoringSessionController();
            var sessionStopped = sessionController.StopMonitoringSessionAsync().Result;
            if (!sessionStopped)
            {
                Logger.LogCpuMonitoringVerboseEvent($"Failed while stopping the session", sessionId);
                m_FailedStoppingSession = true;
            }
            else
            {
                m_MonitoringEnabled = false;
                m_CpuMonitoringRule = null;
                m_FailedStoppingSession = false;

                if (!string.IsNullOrWhiteSpace(sessionId)
                    && analysisNeeded)
                {
                    MonitoringSessionController controller = new MonitoringSessionController();
                    controller.AnalyzeSession(sessionId);
                }
            }
        }

        private static bool CheckMonitoringEnabled()
        {
            MonitoringSessionController sessionController = new MonitoringSessionController();
            var session = sessionController.GetActiveSession();

            if (session == null)
            {
                return false;
            }
            else
            {
                if (m_CpuMonitoringRule == null)
                {
                    m_CpuMonitoringRule = InitializeMonitoringRuleForSession(session);
                }
                else
                {
                    // Check if it is the same session or not
                    if (m_CpuMonitoringRule.SessionId != session.SessionId)
                    {
                        Logger.LogCpuMonitoringVerboseEvent($"Reloading monitoring session as session has changed, old={m_CpuMonitoringRule.SessionId}, new={session.SessionId}", m_CpuMonitoringRule.SessionId);
                        m_CpuMonitoringRule = InitializeMonitoringRuleForSession(session);
                        m_CpuMonitoring.InitializeMonitoring(m_CpuMonitoringRule);
                    }
                }

                return true;
            }
        }

        private static ICpuMonitoringRule InitializeMonitoringRuleForSession(MonitoringSession session)
        {
            if (session.RuleType == RuleType.Diagnostics)
                return new DiagnosticCpuRule(session);

            return new AlwaysOnCpuRule(session);
        }

        private static void DaasRunner_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = (Exception)e.ExceptionObject;
                Logger.LogErrorEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version} terminating with unhandled exception", ex);
            }
            catch
            {
                var strException = new ApplicationException();
                Logger.LogErrorEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version} terminating with unhandled exception object", strException);
            }

        }

        private static void DaasRunnerTerminationHandler(object sender, EventArgs e)
        {
            Logger.LogVerboseEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version} terminated gracefully");
        }

        private static void StartSessionRunner()
        {
            while (true)
            {
                RunActiveSession(_cts.Token);
                RunAnalysisQueuedV2Sessions(_cts.Token);
                RemoveOlderSessionsIfNeeded();
                Thread.Sleep(Settings.Instance.FrequencyToCheckForNewSessionsAtInSeconds * 1000);
            }
        }

        private static void DeleteSessionSafe(Session session, bool isV2Session)
        {
            try
            {
                if (isV2Session)
                {
                    _azureStorageSessionManager.DeleteSessionAsync(session.SessionId).Wait();
                }
                else
                {
                    _sessionManager.DeleteSessionAsync(session.SessionId).Wait();
                }
            }
            catch (Exception)
            {
            }
        }

        private static void RemoveOlderSessionsIfNeeded()
        {
            if (DateTime.UtcNow.Subtract(_lastSessionCleanupTime).TotalHours > Settings.Instance.HoursBetweenOldSessionsCleanup)
            {
                _lastSessionCleanupTime = DateTime.UtcNow;
                List<Session> allSessions;

                try
                {
                    allSessions = _sessionManager.GetAllSessionsAsync().Result.ToList();
                    RemovedOlderSessions(allSessions, isV2Session: false);
                }
                catch (Exception ex)
                {
                    Logger.LogWarningEvent("Failed while getting sessions", ex);
                }

                if (CheckIfTimeToCleanupSymbols())
                {
                    CleanupSymbolsDirectory();
                }

                if (!_azureStorageSessionManager.IsEnabled)
                {
                    return;
                }

                try
                {
                    allSessions = _azureStorageSessionManager.GetCompletedSessionsAsync().Result.ToList();
                    RemovedOlderSessions(allSessions, isV2Session: true);
                }
                catch (Exception ex)
                {
                    Logger.LogWarningEvent("Failed while getting V2 sessions", ex);
                }
            }
        }

        private static void RemovedOlderSessions(List<Session> allSessions, bool isV2Session)
        {
            if (allSessions.Any())
            {
                // Leave the last 'MaxSessionsToKeep' sessions and delete the older sessions
                var olderSessions = allSessions.OrderBy(x => x.StartTime).Take(Math.Max(0, allSessions.Count() - Settings.Instance.MaxSessionsToKeep));
                foreach (var session in olderSessions)
                {
                    DeleteSessionSafe(session, isV2Session);
                }

                // Delete all the sessions older than 'MaxSessionAgeInDays' days
                olderSessions = allSessions.Where(x => DateTime.UtcNow.Subtract(x.StartTime).TotalDays > Settings.Instance.MaxSessionAgeInDays);
                foreach (var session in olderSessions)
                {
                    DeleteSessionSafe(session, isV2Session);
                }
            }
        }

        private static void RunAnalysisQueuedV2Sessions(CancellationToken stoppingToken)
        {
            try
            {
                if (_azureStorageSessionManager.IsEnabled == false)
                {
                    return;
                }

                var activeSession = _azureStorageSessionManager.GetActiveSessionAsync().Result;
                if (activeSession == null)
                {
                    return;
                }

                Logger.LogSessionVerboseEvent("Found an active V2 session", activeSession.SessionId);

                if (DateTime.UtcNow.Subtract(_lastV2SessionUpdatedTime).TotalMinutes > 5 
                    && DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes > 5)
                {
                    _lastV2SessionUpdatedTime = DateTime.UtcNow;

                    Logger.LogSessionVerboseEvent("Entered V2 Session cleanup loop as we have an active session running for > 5 minutes", activeSession.SessionId);

                    var sessionWasUpdated = _azureStorageSessionManager.CancelOrphanedV2InstancesIfNeeded(activeSession).Result;
                    if (sessionWasUpdated)
                    {
                        Logger.LogSessionVerboseEvent("Session was updated so getting the latest", activeSession.SessionId);
                        activeSession = _azureStorageSessionManager.GetActiveSessionAsync().Result;
                        if (activeSession == null)
                        {
                            return;
                        }
                    }

                    var didSessionTimeOutOrComplete = _azureStorageSessionManager.ShouldSessionTimeoutAsync(activeSession).Result;
                    if (didSessionTimeOutOrComplete)
                    {
                        Logger.LogSessionVerboseEvent("The session had either timed out or completed", activeSession.SessionId);
                        if (_runningV2Sessions.ContainsKey(activeSession.SessionId))
                        {
                            Logger.LogSessionVerboseEvent("Cancelling session as MaxSessionTimeInMinutes limit reached", activeSession.SessionId);
                            _runningV2Sessions[activeSession.SessionId].CancellationTokenSource.Cancel();
                        }

                        return;
                    }

                    bool isSessionCompleted = _azureStorageSessionManager.CheckandCompleteSessionIfNeededAsync().Result;
                    if (isSessionCompleted)
                    {
                        Logger.LogSessionVerboseEvent("Session is marked as completed", activeSession.SessionId);
                        return;
                    }
                }

                if (_azureStorageSessionManager.ShouldCollectOnCurrentInstance(activeSession))
                {
                    if (_runningV2Sessions.ContainsKey(activeSession.SessionId))
                    {
                        // analysis for this session is in progress
                        return;
                    }

                    if (!_azureStorageSessionManager.ShouldAnalyzeOnCurrentInstance(activeSession))
                    {
                        return;
                    }

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var sessionTask = _azureStorageSessionManager.AnalyzeAndCompleteSessionAsync(activeSession, activeSession.SessionId, cts.Token);

                    var t = new TaskAndCancellationToken
                    {
                        UnderlyingTask = sessionTask,
                        CancellationTokenSource = cts
                    };

                    _runningV2Sessions[activeSession.SessionId] = t;
                    RemoveOldSessionsFromRunningSessionsList("_runningV2Sessions", _runningV2Sessions);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed in RunAnalysisQueuedV2Sessions", ex);
            }
        }

        private static void RunActiveSession(CancellationToken stoppingToken)
        {
            try
            {
                var activeSession = _sessionManager.GetActiveSessionAsync().Result;
                if (activeSession == null)
                {
                    return;
                }

                Logger.LogSessionVerboseEvent("Found an active session", activeSession.SessionId);

                //
                // Check if all instances are finished with log collection
                //

                if (_runningSessions.ContainsKey(activeSession.SessionId) == false)
                {
                    //
                    // Perform this check only if the current instance is not running the session
                    // as it can lead to race condition to complete the session
                    //

                    if (_sessionManager.CheckandCompleteSessionIfNeededAsync().Result)
                    {
                        return;
                    }
                }

                if (DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes > Settings.Instance.OrphanInstanceTimeoutInMinutes)
                {
                    _sessionManager.CancelOrphanedInstancesIfNeeded().Wait();
                }

                var totalSessionRunningTimeInMinutes = DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes;
                if (DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes > Settings.Instance.MaxSessionTimeInMinutes)
                {
                    if (_runningSessions.ContainsKey(activeSession.SessionId))
                    {
                        Logger.LogSessionVerboseEvent("Cancelling session as MaxSessionTimeInMinutes limit reached", activeSession.SessionId);
                        _runningSessions[activeSession.SessionId].CancellationTokenSource.Cancel();
                    }
                    else
                    {
                        //
                        // Allow 5 minutes additional for the instance to gracefully terminate the session and cancel the task
                        //

                        if (totalSessionRunningTimeInMinutes > (Settings.Instance.MaxSessionTimeInMinutes + 5))
                        {
                            //
                            // If the current instance is not running the session, mark the session as Complete
                            // when MaxSessionTimeInMinutes is hit. This will ensure any long running sessions will
                            // get completed and they will not hang indefinitely
                            //

                            Logger.LogSessionVerboseEvent("Forcefully marking the session as TimedOut as MaxSessionTimeInMinutes limit reached", activeSession.SessionId);
                            _ = _sessionManager.CheckandCompleteSessionIfNeededAsync(forceCompletion: true).Result;
                        }
                    }
                }

                if (_sessionManager.ShouldCollectOnCurrentInstance(activeSession))
                {
                    if (_runningSessions.ContainsKey(activeSession.SessionId))
                    {
                        // data Collection for this session is in progress
                        return;
                    }

                    if (_sessionManager.HasThisInstanceCollectedLogs().Result)
                    {
                        // This instance has already collected logs for this session
                        return;
                    }

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var sessionTask = _sessionManager.RunToolForSessionAsync(activeSession, queueAnalysisRequest: false, cts.Token);

                    var t = new TaskAndCancellationToken
                    {
                        UnderlyingTask = sessionTask,
                        CancellationTokenSource = cts
                    };

                    _runningSessions[activeSession.SessionId] = t;
                    RemoveOldSessionsFromRunningSessionsList("_runningSessions", _runningSessions);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed in RunActiveSession", ex);
            }
        }

        private static void RemoveOldSessionsFromRunningSessionsList(string label, ConcurrentDictionary<string, TaskAndCancellationToken> runningSessions)
        {
            Logger.LogVerboseEvent($"{label}.Count = {runningSessions.Count}");

            foreach (var entry in runningSessions)
            {
                string sessionId = string.Empty;
                if (entry.Value != null && entry.Value.UnderlyingTask != null)
                {
                    var status = entry.Value.UnderlyingTask.Status;
                    if (status == TaskStatus.Canceled || status == TaskStatus.Faulted || status == TaskStatus.RanToCompletion)
                    {
                        sessionId = entry.Key;
                    }
                }
                else
                {
                    sessionId = entry.Key;
                }

                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    if (runningSessions.TryRemove(sessionId, out _))
                    {
                        Logger.LogVerboseEvent($"Task for Session '{sessionId}' removed from {label} list");
                    }
                }
            }
        }
    }

}
