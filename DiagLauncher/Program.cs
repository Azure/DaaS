// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CommandLine;
using DaaS;
using DaaS.Diagnostics;
using DaaS.Sessions;
using DaaS.Storage;
using Newtonsoft.Json;

namespace DiagLauncher
{
    internal class Program
    {
        const string SessionFileNameFormat = "yyMMdd_HHmmssffff";

        static readonly IAzureStorageSessionManager _sessionManager = new AzureStorageSessionManager(new AzureStorageService())
        {
            InvokedViaAutomation = true
        };

        static void Main(string[] args)
        {
            if (args.Length == 0)
                args = new[] { "--help" };

            var parser = new Parser(config => config.HelpWriter = Console.Out);

            var result = parser.ParseArguments<Options>(args).MapResult(
                (opts) => RunDiagnosticTool(opts), 
                errs => { return 0; });
        }

        private static object RunDiagnosticTool(Options options)
        {
            if (options.ListDiagnosers)
            {
                Console.WriteLine("Listing diagnosers...");
                Console.WriteLine();
                foreach (var d in _sessionManager.GetDiagnosers())
                {
                    Console.WriteLine(d.Name);
                    Console.WriteLine("-----------------------------------");
                    Console.WriteLine(d.Description);
                    Console.WriteLine();
                }
            }
            else if (options.ListSessions)
            {
                if (ExitIfSessionManagerDisabled())
                {
                    return 0;
                }

                try
                {
                    Console.WriteLine("Listing sessions...");
                    var sessions = _sessionManager.GetAllSessionsAsync().Result;
                    Console.WriteLine("------------------------------------------------------------------------------------------------");
                    Console.WriteLine("SessionId\t\tStatus\t\tWhen\t\tDuration\tTool");
                    Console.WriteLine("------------------------------------------------------------------------------------------------");
                    foreach (var s in sessions)
                    {
                        Console.WriteLine($"{s.SessionId}\t{GetStatus(s)}\t{GetDateTime(s.StartTime)}\t {GetSessionDuration(s.StartTime, s.EndTime)}\t\t{s.Tool}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed while getting sessions - {ex}");
                }
                
            }
            else if (!string.IsNullOrWhiteSpace(options.SessionIdForDeletion))
            {
                if (ExitIfSessionManagerDisabled())
                {
                    return 0;
                }

                try
                {
                    Console.WriteLine("Deleting session...");
                    _sessionManager.DeleteSessionAsync(options.SessionIdForDeletion).Wait();
                    Console.WriteLine($"Session '{options.SessionIdForDeletion}' deleted");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed while deleting session [{options.SessionIdForDeletion}] - {ex}");
                }

                return 0;
                
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(options.SessionId))
                {
                    Logger.LogSessionVerboseEvent("DiagLauncher started", options.SessionId);
                }

                if (ExitIfSessionManagerDisabled())
                {
                    if (!string.IsNullOrWhiteSpace(options.SessionId))
                    {
                        Logger.LogSessionVerboseEvent("DiagLauncher existing because SessionManager is disabled", options.SessionId);
                    }
                    else
                    {
                        Logger.LogVerboseEvent("DiagLauncher existing because SessionManager is disabled");
                    }
                    return 0;
                }

                Stopwatch sw = new Stopwatch();
                sw.Start();
                var sessionId = CollectLogsAndTakeActions(options.Tool, options.Mode, options.ToolParams, options.SessionId);
                sw.Stop();
                string message = $"DiagLauncher completed after {sw.Elapsed.TotalMinutes:0} minutes!";
                Logger.LogSessionVerboseEvent(message, sessionId);
                Console.WriteLine(message);

                KillProcessIfNeeded(sessionId, options.Mode);
            }

            return 0;
        }

        private static string GetStatus(Session s)
        {
            if (s.Status == Status.Active)
            {
                return s.Status.ToString() + "\t";
            }

            return s.Status.ToString();
        }

        private static bool ExitIfSessionManagerDisabled()
        {
            if (_sessionManager.IsEnabled == false)
            {
                Console.WriteLine("The App setting 'WEBSITE_DAAS_STORAGE_CONNECTIONSTRING' does not exist so existing...");
                return true;
            }

            return false;
        }

        private static void KillProcessIfNeeded(string sessionId, string mode)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = "SESSION_THROTTLED";
            }

            if (!string.IsNullOrWhiteSpace(mode) && Enum.TryParse(mode, out Mode sessionMode))
            {
                if (sessionMode != Mode.CollectKillAnalyze)
                {
                    Logger.LogSessionVerboseEvent($"Session Mode is {sessionMode} and does not require killing the process", sessionId);
                    return;
                }

                Process mainSiteW3wpProcess = GetMainSiteW3wpProcess(sessionId);
                Console.WriteLine($"Killing process {mainSiteW3wpProcess.ProcessName} with pid {mainSiteW3wpProcess.Id}");
                mainSiteW3wpProcess.Kill();
                Logger.LogSessionVerboseEvent($"DaasLauncher killed process {mainSiteW3wpProcess.ProcessName} with pid {mainSiteW3wpProcess.Id}", sessionId);
            }
        }

        private static Process GetMainSiteW3wpProcess(string sessionId)
        {
            Logger.LogSessionVerboseEvent("Getting main site's w3wp process", sessionId);

            bool inScmSite = false;
            var homePath = Environment.GetEnvironmentVariable("HOME_EXPANDED");
            string siteName = Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME") != null ? Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME").ToString() : "";
            if (homePath.Contains(@"\DWASFiles\Sites\#") || siteName.StartsWith("~"))
            {
                inScmSite = true;
            }

            var parentProcess = Process.GetCurrentProcess();
            Process mainSiteW3wpProcess = null;
            while (parentProcess != null)
            {
                if (!parentProcess.ProcessName.Equals("w3wp", StringComparison.OrdinalIgnoreCase))
                {
                    parentProcess = parentProcess.GetParentProcess();
                    continue;
                }

                if (inScmSite)
                {
                    mainSiteW3wpProcess = Process.GetProcessesByName("w3wp").FirstOrDefault(p => p.Id != parentProcess.Id);
                }
                else
                {
                    mainSiteW3wpProcess = parentProcess;
                }
                break;
            }

            if (mainSiteW3wpProcess == null)
            {
                Logger.LogSessionVerboseEvent("The worker process for the main site is not running any more", sessionId);
            }

            return mainSiteW3wpProcess;
        }

        private static string CollectLogsAndTakeActions(string tool, string mode, string toolParams, string sessionId)
        {
            Logger.LogVerboseEvent($"DiagLauncher started with tool-{tool}, mode-{mode}, toolParams-{toolParams}, sessionId-{sessionId}");
            if (string.IsNullOrWhiteSpace(tool))
            try
            {
                ThrowIfRequiredSettingsMissing();
                return SubmitAndWaitForSession(tool, mode, toolParams, sessionId);
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex =>
                {
                    LogRealException(ex);
                    return true;
                });
            }
            catch (Exception ex)
            {
                LogRealException(ex);
            }

            return string.Empty;
        }

        private static void ThrowIfRequiredSettingsMissing()
        {
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

        private static string SubmitAndWaitForSession(string tool, string mode, string toolParams, string sessionId)
        {
            _sessionManager.ThrowIfMultipleDiagLauncherRunning(Process.GetCurrentProcess().Id);
            CancellationTokenSource cts = new CancellationTokenSource();

            if (string.IsNullOrWhiteSpace(sessionId))
            {

                //
                // This is the code path to take when DiagLauncher is invoked via Auto-Heal
                // directly. In this case, DiagLauncher will create a new session and submit
                //

                Mode sessionMode = GetToolMode(mode);

                var session = new Session()
                {
                    SessionId = DateTime.UtcNow.ToString(SessionFileNameFormat),
                    Instances = new List<string>() { Environment.MachineName },
                    Tool = tool,
                    ToolParams = toolParams,
                    Mode = sessionMode,
                    Description = GetSessionDescription(),
                };
                
                sessionId = _sessionManager.SubmitNewSessionAsync(session).Result;
                Console.WriteLine($"Session submitted for '{tool}' with Id - {sessionId}");

                var details = new
                {
                    Diagnoser = tool,
                    InstancesSelected = Environment.MachineName,
                    Options = mode
                };

                var detailsString = JsonConvert.SerializeObject(details);
                Logger.LogDaasConsoleEvent("DiagLauncher started a new Session", detailsString, sessionId);
                EventLog.WriteEntry("Application", $"DiagLauncher started with {detailsString} ", EventLogEntryType.Information);
            }

            Logger.LogSessionVerboseEvent($"DiagLauncher started for session on instance {Environment.MachineName}", sessionId);

            bool queueAnalysisRequest = mode == "CollectKillAnalyze";
            if (queueAnalysisRequest)
            {
                Logger.LogSessionVerboseEvent("Session mode is CollectKillAnalyze", sessionId);
                CopyDaasRunnerIfNeeded(sessionId);
            }

            Console.WriteLine($"Running session - {sessionId}");

            try
            {
                _sessionManager.RunActiveSessionAsync(queueAnalysisRequest, cts.Token).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed while running session with {ex}" );
                Logger.LogSessionErrorEvent("Failed while running session", ex, sessionId);
                throw;
            }

            return sessionId;
        }

        private static void CopyDaasRunnerIfNeeded(string sessionId)
        {
            var existingDaasRunner = EnvironmentVariables.DaasRunner;
            if (FileSystemHelpers.FileExists(existingDaasRunner))
            {
                Logger.LogSessionVerboseEvent("DaasRunner already exists as a webjob", sessionId);
                return;
            }

            SessionController sessionController = new SessionController();
            sessionController.StartSessionRunner();
        }

        private static Mode GetToolMode(string mode)
        {
            if (Enum.TryParse(mode, out Mode collectionMode))
            {
                return collectionMode;
            }

            throw new ArgumentException($"Invalid diagnostic mode specified. Allowed options are [{string.Join(",", Enum.GetNames(typeof(Mode)))}]");
        }

        private static string GetSessionDescription()
        {
            string desc = "InvokedViaDaasConsole";
            string val = Environment.GetEnvironmentVariable("WEBSITE_AUTOHEAL_REASON");
            if (!string.IsNullOrWhiteSpace(val))
            {
                desc += $"-{val}";
            }

            return desc;
        }

        private static void LogRealException(Exception ex)
        {
            string logMessage = $"Unhandled exception in DiagLauncher.exe - {ex} ";
            EventLog.WriteEntry("Application", logMessage, EventLogEntryType.Information);
            Console.WriteLine(logMessage);
            Logger.LogErrorEvent("Unhandled exception in DiagLauncher.exe while collecting logs and taking actions", ex);
        }

        private static string GetSessionDuration(DateTime startTime, DateTime? endTime)
        {
            if (endTime.HasValue && endTime.Value != DateTime.MinValue)
            {
                return (endTime.Value - startTime).TotalMinutes.ToString("0.00") +"m";
            }
            else
            {
                return "...";
            }
        }

        private static string GetDateTime(DateTime startTime)
        {
            // Get Date Time as days, hours or minutes ago

            var timeSpan = DateTime.UtcNow - startTime;

            if (timeSpan.Days > 0)
            {
                return $"{timeSpan.Days} days ago";
            }
            else if (timeSpan.Hours > 0)
            {
                return $"{timeSpan.Hours} hours ago";
            }
            else if (timeSpan.Minutes > 0)
            {
                return $"{timeSpan.Minutes} minutes ago";
            }
            else
            {
                return "Just now";
            }
        }
    }
}
