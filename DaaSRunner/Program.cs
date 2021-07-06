//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using DaaS;
using DaaS.HeartBeats;
using DaaS.Sessions;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DaaSRunner
{
    class Program
    {
        enum Verbosity
        {
            Information,
            Diagnostic
        }

        private static Verbosity VerbosityLevel = Verbosity.Information;
        private static SessionController _DaaS = new SessionController();
        private static int cleanOutHeartBeats = 0;
        private static double sleepIntervalForHeartbeatCheck = 0;
        private static DateTime _lastHeartbeatSent = DateTime.MinValue;
        private static DateTime _lastInstanceCountCheck = DateTime.MinValue;
        private static int InstanceCountCheckFrequency = 30;
        private static bool m_MonitoringEnabled = false;
        private static readonly CpuMonitoring m_CpuMonitoring = new CpuMonitoring();
        private static MonitoringSession m_MonitoringSession = null;
        private static bool m_FailedStoppingSession = false;
        private static bool m_SecretsCleared;
        private static Timer m_SasUriTimer;
        private static Timer m_CompletedSessionsCleanupTimer;

        static void Main(string[] args)
        {
            Logger.LogVerboseEvent($"DaasRunner.exe with version {Assembly.GetExecutingAssembly().GetName().Version.ToString() } and ProcessId={ Process.GetCurrentProcess().Id } started");
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
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

            if (args.Length > 0)
            {
                if (args[0].Equals("-v", StringComparison.OrdinalIgnoreCase))
                {
                    VerbosityLevel = Verbosity.Diagnostic;
                }
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

        private static bool ValidateSasUri(string sasUri, bool isdefinedInEnvironmentVariable)
        {
            var result = DaaS.Storage.BlobController.ValidateBlobSasUri(sasUri, out Exception _);
            Logger.LogVerboseEvent($"BlobStorageSasUri at EnvironmentVariable={isdefinedInEnvironmentVariable} is Valid={result}");
            return result;
        }

        private static void ClearSecretIfNeeded(string sasUriPrivateSettings , bool secretInvalid)
        {
            if (!m_SecretsCleared && !string.IsNullOrWhiteSpace(sasUriPrivateSettings))
            {
                _DaaS.BlobStorageSasUri = string.Empty;
                if (secretInvalid)
                {
                    Logger.LogVerboseEvent("Cleared SAS URI from PrivateSettings.xml as it is invalid");
                }
                else
                {
                    Logger.LogVerboseEvent("Cleared SAS URI from PrivateSettings.xml as a valid SAS URI exists as an Environment variable");
                }
                m_SecretsCleared = true;
            }
        }

        private static void ValidateSasAccounts(object state)
        {
            try
            {
                string sasUriPrivateSettings = _DaaS.BlobStorageSasUri;
                var sasUriEnvironment = _DaaS.GetBlobSasUriFromEnvironment(out bool sasUriInEnvironmentVariable);

                if (!string.IsNullOrWhiteSpace(sasUriEnvironment) && sasUriInEnvironmentVariable)
                {
                    if (ValidateSasUri(sasUriEnvironment, isdefinedInEnvironmentVariable: true))
                    {
                        ClearSecretIfNeeded(sasUriPrivateSettings, secretInvalid: false);
                        return;
                    }
                }

                //
                // We reach here only if SAS URI in environment is not defined or is not valid
                //

                if (!string.IsNullOrWhiteSpace(sasUriPrivateSettings))
                {
                    if (!ValidateSasUri(sasUriPrivateSettings, isdefinedInEnvironmentVariable: false))
                    {
                        ClearSecretIfNeeded(sasUriPrivateSettings, secretInvalid:true);
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
            int maxSessionsToKeep = _DaaS.MaxDiagnosticSessionsToKeep;
            int numberOfDays = _DaaS.MaxNumberOfDaysForSessions;
            var completedSessions = _DaaS.GetAllInActiveSessions().OrderByDescending(s => s.StartTime).ToList();
            MonitoringSessionController controllerMonitoring = new MonitoringSessionController();
            var completedMonitoringSessions = controllerMonitoring.GetAllCompletedSessions().OrderByDescending(s => s.StartDate).ToList();

            Logger.LogVerboseEvent($"Starting cleanup for Completed Sessions MaxDiagnosticSessionsToKeep = [{maxSessionsToKeep}] MaxNumberOfDaysForSessions= [{numberOfDays}]");
            List<Session> sessionsToRemove = completedSessions.Skip(maxSessionsToKeep).ToList();
            string logMessage = $"[MaxDiagnosticSessionsToKeep] Found {sessionsToRemove.Count()} sessions to remove as we have {completedSessions.Count()} completed sessions";
            DeleteSessions(sessionsToRemove, async (session) => { await _DaaS.Delete(session); }, logMessage);

            completedSessions = _DaaS.GetAllInActiveSessions().OrderByDescending(s => s.StartTime).ToList();

            if (CheckIfTimeToCleanupSymbols(completedSessions))
            {
                CleanupSymbolsDirectory();
            }

            List<Session> olderSessions = completedSessions.Where(x => x.StartTime < DateTime.UtcNow.AddDays(-1 * numberOfDays)).ToList();
            logMessage = $"[MaxNumberOfDaysForSessions] Found {olderSessions.Count()} older sessions to remove";
            DeleteSessions(olderSessions, async (session) => { await _DaaS.Delete(session); }, logMessage);

            List<MonitoringSession> monitoringSessionsToRemove = completedMonitoringSessions.Skip(maxSessionsToKeep).ToList();
            logMessage = $"[MaxDiagnosticSessionsToKeep] Found {monitoringSessionsToRemove.Count()} monitoring sessions to remove as we have {completedMonitoringSessions.Count()} completed sessions";
            DeleteSessions(monitoringSessionsToRemove, (session) => { controllerMonitoring.DeleteSession(session.SessionId); }, logMessage);

            completedMonitoringSessions = controllerMonitoring.GetAllCompletedSessions().OrderByDescending(s => s.StartDate).ToList();
            List<MonitoringSession> olderSessionsMonitoring = completedMonitoringSessions.Where(x => x.StartDate < DateTime.UtcNow.AddDays(-1 * numberOfDays)).ToList();
            logMessage = $"[MaxNumberOfDaysForSessions] Found {olderSessionsMonitoring.Count()} older monitoring sessions to remove";
            DeleteSessions(olderSessionsMonitoring, (session) => { controllerMonitoring.DeleteSession(session.SessionId); }, logMessage);

        }

        private static bool CheckIfTimeToCleanupSymbols(List<Session> completedSessions)
        {
            if (_DaaS.GetAllActiveSessions().Count() != 0)
            {
                return false;
            }
            var cleanupSymbols = false;
            var lastCompletedSession = completedSessions.FirstOrDefault();
            if (lastCompletedSession != null)
            {
                if (DateTime.UtcNow.Subtract(lastCompletedSession.StartTime).TotalDays > 1)
                {
                    cleanupSymbols = true;
                }
            }
            else
            {
                cleanupSymbols = true;
            }
            return cleanupSymbols;
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
                            m_CpuMonitoring.InitializeMonitoring(m_MonitoringSession);
                        }

                        if (m_MonitoringEnabled && m_MonitoringSession != null)
                        {
                            if (!m_FailedStoppingSession)
                            {
                                bool shouldExit = m_CpuMonitoring.MonitorCpu(m_MonitoringSession);
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

                if (m_MonitoringEnabled && m_MonitoringSession != null)
                {
                    Logger.LogDiagnostic($"Monitoring is enabled, sleeping for {m_MonitoringSession.MonitorDuration * 1000} milliseconds");
                    Thread.Sleep(m_MonitoringSession.MonitorDuration * 1000);
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
            SessionMode sessionMode = SessionMode.Collect;
            string sessionId = "";
            if (m_MonitoringSession != null)
            {
                sessionMode = m_MonitoringSession.Mode;
                sessionId = m_MonitoringSession.SessionId;
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
                var blobSasUri = m_MonitoringSession.BlobSasUri;
                m_MonitoringEnabled = false;
                m_MonitoringSession = null;
                m_FailedStoppingSession = false;
                if (sessionMode == SessionMode.CollectKillAndAnalyze && !string.IsNullOrWhiteSpace(sessionId))
                {
                    MonitoringSessionController controller = new MonitoringSessionController();
                    controller.AnalyzeSession(sessionId, blobSasUri);
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
                if (m_MonitoringSession == null)
                {
                    m_MonitoringSession = session;
                }
                else
                {
                    // Check if it is the same session or not
                    if (m_MonitoringSession.SessionId != session.SessionId)
                    {
                        Logger.LogCpuMonitoringVerboseEvent($"Reloading monitoring session as session has changed, old={m_MonitoringSession.SessionId}, new={session.SessionId}", m_MonitoringSession.SessionId);
                        m_MonitoringSession = session;
                        m_CpuMonitoring.InitializeMonitoring(m_MonitoringSession);
                    }
                }

                return true;
            }
        }

        private static void DaasRunner_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                Exception ex = (Exception)e.ExceptionObject;
                Logger.LogErrorEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version.ToString() } terminating with unhandled exception", ex);
            }
            catch
            {
                var strException = new ApplicationException();
                Logger.LogErrorEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version.ToString() } terminating with unhandled exception object", strException);
            }

        }

        private static void DaasRunnerTerminationHandler(object sender, EventArgs e)
        {
            Logger.LogVerboseEvent($"DaasRunner with version {Assembly.GetExecutingAssembly().GetName().Version.ToString() } terminated gracefully");
        }

        private static void StartSessionRunner()
        {
            int logCounter = 0;
            sleepIntervalForHeartbeatCheck = (sleepIntervalForHeartbeatCheck < _DaaS.FrequencyToCheckForNewSessionsAt.TotalSeconds) ? _DaaS.FrequencyToCheckForNewSessionsAt.TotalSeconds : sleepIntervalForHeartbeatCheck;
            _lastInstanceCountCheck = DateTime.UtcNow;

            while (true)
            {
                Logger.LogDiagnostic("Checking for active sessions...");
                var activeSessions = _DaaS.GetAllActiveSessions().ToList();
                if (activeSessions.Count == 0)
                {
                    Logger.LogDiagnostic("No active sessions");

                    var cancelledInstances = _DaaS.GetCancelledInstances();
                    if (cancelledInstances != null && cancelledInstances.Count > 0)
                    {
                        Logger.LogDiagnostic($"Found {cancelledInstances.Count} cancelled instances with instance names = { string.Join(",", cancelledInstances.Select(x => x.Name)) }");
                        var anyInstanceToCancel = cancelledInstances.FirstOrDefault();
                        if (anyInstanceToCancel != null)
                        {
                            _DaaS.CancelActiveSessionOnThisInstance(anyInstanceToCancel.SessionId);
                        }

                        RemoveOldCancelledFiles(cancelledInstances);
                        KillChildProcessesIfAnyForCancelledInstances(cancelledInstances);
                    }

                }
                else
                {
                    // ensure that we don't end up submitting too many sessions
                    // This can happen if AutoHealing has generated too many files
                    int activeSessionCount = activeSessions.Count;
                    if (activeSessionCount > 5)
                    {
                        foreach (var session in activeSessions.Skip(5))
                        {
                            Logger.LogSessionVerboseEvent($"Deleting Session [{ session.SessionId }] because there are more than { activeSessionCount } sessions active", session.SessionId.ToString());
                            try
                            {
                                var task = Task.Run(async () => { await _DaaS.Delete(session, true); });
                                task.Wait();
                            }
                            catch (Exception ex)
                            {

                                Logger.LogSessionErrorEvent("Failed while deleting Session", ex, session.SessionId.ToString());
                            }
                        }
                    }
                }

                foreach (var session in activeSessions)
                {
                    Logger.LogInfo("  Session " + session.SessionId + " - Status: " + session.Status);
                    Logger.LogInfo("      " + session.FullPermanentStoragePath);
                }
                try
                {
                    _DaaS.RunActiveSessions();
                    if (DateTime.UtcNow.Subtract(_lastHeartbeatSent).TotalSeconds > sleepIntervalForHeartbeatCheck)
                    {
                        _lastHeartbeatSent = DateTime.UtcNow;
                        SendHeartBeat();
                    }
                }
                catch (Exception e)
                {
                    Logger.LogErrorEvent("Encountered unhandled exception while running sessions", e);
                }

                if (DateTime.UtcNow.Subtract(_lastInstanceCountCheck).TotalMinutes > InstanceCountCheckFrequency)
                {
                    _lastInstanceCountCheck = DateTime.UtcNow;
                    try
                    {
                        logCounter++;
                        int instanceCount = HeartBeatController.GetNumberOfLiveInstances();
                        sleepIntervalForHeartbeatCheck = GetSleepIntervalBetweenHeartbeats(instanceCount);

                        // Treat sleepInterval as minutes for InstanceCountCheckFrequency 
                        // to avoid making this call every 30 minutes
                        InstanceCountCheckFrequency = Convert.ToInt32(sleepIntervalForHeartbeatCheck);
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

                Logger.LogDiagnostic("Finished iteration");
                Thread.Sleep(_DaaS.FrequencyToCheckForNewSessionsAt);
            }
        }

        private static double GetSleepIntervalBetweenHeartbeats(int instanceCount)
        {
            int interval = (instanceCount / 10) * 30;
            interval = Math.Max(30, interval);
            interval = Math.Min(180, interval);
            return Convert.ToDouble(interval);
        }

        private static void RemoveOldCancelledFiles(List<CancelledInstance> cancelledInstances)
        {
            try
            {
                foreach (var item in cancelledInstances)
                {
                    if (DateTime.UtcNow.Subtract(item.CancellationTime).TotalMinutes > 15)
                    {
                        item.DeleteFile().Wait();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed while deleting old cancelled file", ex);
            }
        }

        private static void KillChildProcessesIfAnyForCancelledInstances(List<CancelledInstance> cancelledInstances)
        {
            try
            {
                var instance = cancelledInstances.FirstOrDefault(x => x.Name == Environment.MachineName);
                if (instance == null)
                {
                    // Not this instance so return
                    return;
                }

                // Even if we succeed or not, lets try killing processes again
                instance.DeleteFile().Wait();

                var currentProcess = Process.GetCurrentProcess();

                if (instance.ProcessCleanupOnCancel == null)
                {
                    Logger.LogVerboseEvent($"ProcessCleanupOnCancel is NULL so it is probably an older session ");
                    return;
                }

                Logger.LogVerboseEvent($"Found ProcessCleanupOnCancel = {instance.ProcessCleanupOnCancel} ");
                List<string> processToKill = instance.ProcessCleanupOnCancel.ToLower().Split(',').ToList();

                foreach (var runningProcess in Process.GetProcesses())
                {
                    // this should never be the case but let's not kill any w3wp.exe process
                    if (runningProcess.ProcessName.Equals("w3wp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        if (processToKill.Contains(runningProcess.ProcessName.ToLower()))
                        {
                            runningProcess.Kill();
                            Logger.LogVerboseEvent($"Successfully killed process {runningProcess.ProcessName.ToLower()} with ID - {runningProcess.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorEvent($"Failed while killing process {runningProcess.ProcessName.ToLower()} with ID - {runningProcess.Id}", ex);
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed while killing child processes for cancelled sessions", ex);
            }

        }

        private static void SendHeartBeat()
        {
            if (VerbosityLevel >= Verbosity.Information)
            {
                Logger.LogDiagnostic("Sending Heartbeat...");
            }

            HeartBeatController.SendHeartBeat();

            Logger.LogDiagnostic("Sent heartbeat");

            // No need to bother cleaning out stale heartbeats all the time. 
            cleanOutHeartBeats++;
            if (cleanOutHeartBeats >= 5)
            {
                Logger.LogDiagnostic("Cleaning out stale heartbeats (if there are any)");
                HeartBeatController.DeleteExpiredHeartBeats();
                cleanOutHeartBeats = 0;
            }
            else
            {
                Logger.LogDiagnostic("Clean heart beat counter: " + cleanOutHeartBeats);
            }
        }
    }

}
