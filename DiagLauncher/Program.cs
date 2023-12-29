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
using Newtonsoft.Json;

namespace DiagLauncher
{
    internal class Program
    {
        static readonly ISessionManager _sessionManager = new SessionManager()
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

        private static object RunDiagnosticTool(Options o)
        {
            if (o.ListDiagnosers)
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
            else
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                var sessionId = CollectLogsAndTakeActions(o.Tool, o.Mode, o.ToolParams, o.SessionId);
                KillProcessIfNeeded(sessionId, o.Mode);
                Console.WriteLine("Waiting for completion...");
                WaitForSessionCompletion(sessionId, exitIfAnalyzing: false);

                sw.Stop();
                string message = $"DiagLauncher completed after {sw.Elapsed.TotalMinutes:0} minutes!";
                Logger.LogSessionVerboseEvent(message, sessionId); 
                Console.WriteLine(message);
            }

            return 0;
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

                var session = new Session()
                {
                    Instances = new List<string>() { Environment.MachineName },
                    Tool = tool,
                    ToolParams = toolParams,
                    Mode = GetToolMode(mode),
                    Description = GetSessionDescription(),
                };

                sessionId = _sessionManager.SubmitNewSessionAsync(session, isV2Session: true).Result;
                Logger.LogSessionVerboseEvent("DiagLauncher submitted a new session", sessionId);
                Console.WriteLine($"DiagLauncher submitted a new session-{sessionId} for '{tool}'");

                var details = new
                {
                    Diagnoser = tool,
                    InstancesSelected = Environment.MachineName,
                    Options = mode
                };

                var detailsString = JsonConvert.SerializeObject(details);
                Logger.LogDaasConsoleEvent("DiagLauncher started a new Session", detailsString);
                EventLog.WriteEntry("Application", $"DiagLauncher started with {detailsString} ", EventLogEntryType.Information);
            }

            _ = _sessionManager.RunActiveSessionAsync(cts.Token);

            WaitForSessionCompletion(sessionId, exitIfAnalyzing: true);
            return sessionId;
        }

        private static void WaitForSessionCompletion(string sessionId, bool exitIfAnalyzing)
        {
            while (true)
            {
                Thread.Sleep(15000);

                try
                {
                    var activeSession = _sessionManager.GetActiveSessionAsync(isV2Session: true).Result;
                    if (activeSession == null)
                    {
                        return;
                    }

                    bool isSessionAnalyzing = _sessionManager.CheckIfAnyInstanceAnalyzing(activeSession);
                    if (exitIfAnalyzing && isSessionAnalyzing)
                    {
                        return;
                    }

                    if (activeSession.ActiveInstances != null)
                    {
                        var currentInstance = activeSession.ActiveInstances.FirstOrDefault(x => x.Name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase));
                        if (currentInstance != null && (currentInstance.Status == Status.Complete || currentInstance.Status == Status.TimedOut))
                        {
                            return;
                        }
                    }

                    _sessionManager.CheckIfOrphaningOrTimeoutNeededAsync(activeSession).Wait();
                }
                catch (Exception ex)
                {
                    Logger.LogSessionErrorEvent("Exception while waiting for active session to complete", ex, sessionId);
                    Console.WriteLine($"Encountered exception while waiting for active session to complete - {ex}");
                }
            }
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
    }
}
