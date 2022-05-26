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
using DaaS.HeartBeats;
using DaaS.Configuration;

namespace DaaSRunner
{
    class Program
    {
        enum Verbosity
        {
            Information,
            Diagnostic
        }

        private static SessionController _DaaS = new SessionController();
        private static int cleanOutHeartBeats = 0;
        private static double sleepIntervalForHeartbeatCheck = 0;
        private static DateTime _lastHeartbeatSent = DateTime.MinValue;
        private static DateTime _lastInstanceCountCheck = DateTime.MinValue;
        private static int _instanceCountCheckFrequency = 30;
        private static bool m_MonitoringEnabled = false;
        private static readonly CpuMonitoring m_CpuMonitoring = new CpuMonitoring();
        private static ICpuMonitoringRule m_CpuMonitoringRule = null;
        private static bool m_FailedStoppingSession = false;
        private static Timer m_SasUriTimer;
        private static Timer m_CompletedSessionsCleanupTimer;

        private static DateTime _lastSessionCleanupTime = DateTime.UtcNow;

        private static readonly ISessionManager _sessionManager = new SessionManager();
        private static readonly ConcurrentDictionary<string, TaskAndCancellationToken> _runningSessions = new ConcurrentDictionary<string, TaskAndCancellationToken>();
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

        static void Main(string[] args)
        {
            Logger.LogVerboseEvent($"DaasRunner.exe with version {Assembly.GetExecutingAssembly().GetName().Version } and ProcessId={ Process.GetCurrentProcess().Id } started");

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
            m_SasUriTimer = new Timer(new TimerCallback(ValidateSasAccounts), null, (int)TimeSpan.FromMinutes(1).TotalMilliseconds, (int)TimeSpan.FromHours(1).TotalMilliseconds);

            // Start a timer to cleanup completed sessions
            m_CompletedSessionsCleanupTimer = new Timer(new TimerCallback(CompletedSessionCleanup), null, (int)TimeSpan.FromMinutes(1).TotalMilliseconds, (int)TimeSpan.FromMinutes(30).TotalMilliseconds);

            InitializeThreadsForCpuMonitoring();

            // Queue a one time operation to clear any *.diaglog files on the Blob
            ThreadPool.QueueUserWorkItem(new WaitCallback(_DaaS.RemoveOlderFilesFromBlob));

            StartSessionRunner();
        }

        private static bool ValidateSasUri(string sasUri)
        {
            var result = DaaS.Storage.BlobController.ValidateBlobSasUri(sasUri, out Exception _);
            Logger.LogVerboseEvent($"BlobStorageSasUri is Valid={result}");
            return result;
        }

        private static void ValidateSasAccounts(object state)
        {
            try
            {
                var sasUriEnvironment = _DaaS.BlobStorageSasUri;
                if (!string.IsNullOrWhiteSpace(sasUriEnvironment))
                {
                    if (ValidateSasUri(sasUriEnvironment))
                    {
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Encountered exception while validating SAS keys", ex);
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
            DeleteSessions(monitoringSessionsToRemove, (session) => { controllerMonitoring.DeleteSession(session.SessionId); }, logMessage);

            completedMonitoringSessions = controllerMonitoring.GetAllCompletedSessions().OrderByDescending(s => s.StartDate).ToList();
            List<MonitoringSession> olderSessionsMonitoring = completedMonitoringSessions.Where(x => x.StartDate < DateTime.UtcNow.AddDays(-1 * numberOfDays)).ToList();
            logMessage = $"[MaxNumberOfDaysForSessions] Found {olderSessionsMonitoring.Count()} older monitoring sessions to remove";
            DeleteSessions(olderSessionsMonitoring, (session) => { controllerMonitoring.DeleteSession(session.SessionId); }, logMessage);

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
                    Logger.LogDiagnostic($"Exception in actual monitoring task : { ex.ToLogString() }");
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
            var sessionStopped = sessionController.StopMonitoringSession();
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
                Logger.LogErrorEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version } terminating with unhandled exception object", strException);
            }

        }

        private static void DaasRunnerTerminationHandler(object sender, EventArgs e)
        {
            Logger.LogVerboseEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version} terminated gracefully");
        }

        private static void StartSessionRunner()
        {
            int logCounter = 0;
            sleepIntervalForHeartbeatCheck = (sleepIntervalForHeartbeatCheck < Settings.Instance.FrequencyToCheckForNewSessionsAtInSeconds) ? Settings.Instance.FrequencyToCheckForNewSessionsAtInSeconds: sleepIntervalForHeartbeatCheck;
            _lastInstanceCountCheck = DateTime.UtcNow;

            while (true)
            {
                if (DateTime.UtcNow.Subtract(_lastHeartbeatSent).TotalSeconds > sleepIntervalForHeartbeatCheck)
                {
                    _lastHeartbeatSent = DateTime.UtcNow;
                    SendHeartBeat();
                }

                if (DateTime.UtcNow.Subtract(_lastInstanceCountCheck).TotalMinutes > _instanceCountCheckFrequency)
                {
                    _lastInstanceCountCheck = DateTime.UtcNow;
                    try
                    {
                        logCounter++;
                        int instanceCount = HeartBeatController.GetNumberOfLiveInstances();
                        sleepIntervalForHeartbeatCheck = GetSleepIntervalBetweenHeartbeats(instanceCount);

                        // Treat sleepInterval as minutes for InstanceCountCheckFrequency 
                        // to avoid making this call every 30 minutes
                        _instanceCountCheckFrequency = Convert.ToInt32(sleepIntervalForHeartbeatCheck);
                        if (logCounter == 5)
                        {
                            logCounter = 0;
                            Logger.LogVerboseEvent($"Live Instance Count = {instanceCount} and sleepInterval between heartbeats = {sleepIntervalForHeartbeatCheck}");
                        }

                    }
                    catch (Exception)
                    {
                    }
                }

                RunActiveSession(_cts.Token);
                RemoveOlderSessionsIfNeeded();

                Thread.Sleep(Settings.Instance.FrequencyToCheckForNewSessionsAtInSeconds * 1000);
            }
        }

        private static void DeleteSessionSafe(Session session)
        {
            try
            {
                _sessionManager.DeleteSessionAsync(session.SessionId).Wait();
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
                var allSessions = new List<Session>();

                try
                {
                    allSessions = _sessionManager.GetAllSessionsAsync().Result.ToList();
                }
                catch (Exception ex)
                {
                    Logger.LogWarningEvent("Failed while getting sessions", ex);
                }

                if (allSessions.Any())
                {
                    // Leave the last 'MaxSessionsToKeep' sessions and delete the older sessions
                    var olderSessions = allSessions.OrderBy(x => x.StartTime).Take(Math.Max(0, allSessions.Count() - Settings.Instance.MaxSessionsToKeep));
                    foreach (var session in olderSessions)
                    {
                        DeleteSessionSafe(session);
                    }

                    // Delete all the sessions older than 'MaxSessionAgeInDays' days
                    olderSessions = allSessions.Where(x => DateTime.UtcNow.Subtract(x.StartTime).TotalDays > Settings.Instance.MaxSessionAgeInDays);
                    foreach (var session in olderSessions)
                    {
                        DeleteSessionSafe(session);
                    }
                }

                if (CheckIfTimeToCleanupSymbols())
                {
                    CleanupSymbolsDirectory();
                }
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

                // Check if all instances are finished with log collection
                if (_sessionManager.CheckandCompleteSessionIfNeededAsync().Result)
                {
                    return;
                }

                if (DateTime.UtcNow.Subtract(activeSession.StartTime).TotalMinutes > Settings.Instance.OrphanInstanceTimeoutInMinutes)
                {
                    _sessionManager.CancelOrphanedInstancesIfNeeded(activeSession).Wait();
                }

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
                        // If the current instance is not running the session, mark the session as Complete
                        // when MaxSessionTimeInMinutes is hit. This will ensure any long running sessions will
                        // get completed and they will not hang indefinitely
                        //

                        Logger.LogSessionVerboseEvent("Forcefully marking the session as TimedOut as MaxSessionTimeInMinutes limit reached", activeSession.SessionId);

                        _ = _sessionManager.CheckandCompleteSessionIfNeededAsync(forceCompletion: true).Result;
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
                    var sessionTask = _sessionManager.RunToolForSessionAsync(activeSession, cts.Token);

                    var t = new TaskAndCancellationToken
                    {
                        UnderlyingTask = sessionTask,
                        CancellationTokenSource = cts
                    };

                    _runningSessions[activeSession.SessionId] = t;
                    RemoveOldSessionsFromRunningSessionsList();
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed in RunActiveSession", ex);
            }
        }

        private static void RemoveOldSessionsFromRunningSessionsList()
        {
            Logger.LogVerboseEvent($"_runningSessions.Count = {_runningSessions.Count}");

            foreach (var entry in _runningSessions)
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
                    if (_runningSessions.TryRemove(sessionId, out _))
                    {
                        Logger.LogVerboseEvent($"Task for Session '{sessionId}' removed from _runningSessions list");
                    }
                }
            }
        }

        private static double GetSleepIntervalBetweenHeartbeats(int instanceCount)
        {
            int interval = (instanceCount / 10) * 30;
            interval = Math.Max(30, interval);
            interval = Math.Min(180, interval);
            return Convert.ToDouble(interval);
        }

        private static void SendHeartBeat()
        {
            HeartBeatController.SendHeartBeat();

            // No need to bother cleaning out stale heartbeats all the time. 
            cleanOutHeartBeats++;
            if (cleanOutHeartBeats >= 5)
            {
                HeartBeatController.DeleteExpiredHeartBeats();
                cleanOutHeartBeats = 0;
            }
        }
    }

}
