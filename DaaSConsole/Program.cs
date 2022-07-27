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
using Newtonsoft.Json;

using System.Net;
using DaaS;
using DaaS.Sessions;

namespace DaaSConsole
{
    class Program
    {
        static readonly SessionManager SessionManager = new SessionManager()
        {
            InvokedViaAutomation = true
        };

        enum Options
        {
            CollectLogs,
            Troubleshoot,
            CollectKillAnalyze,
            ListSessions,
            ListDiagnosers,
            GetSasUri,
            Setup,
            GetSetting,
            Help,
            AllInstances,
            BlobSasUri
        }

        private class Argument
        {
            public Options Command;
            public string Usage;
            public string Description;
        }

        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            int argNum = 0;

            while (argNum < args.Length)
            {
                Options option;
                var currentArgument = args[argNum];
                if (!ArgumentIsAParameter(currentArgument))
                {
                    ShowUsage();
                    return;
                }

                if (!Options.TryParse(args[argNum].Substring(1, args[argNum].Length - 1), true, out option))
                {
                    ShowUsage();
                    return;
                }

                argNum++;

                switch (option)
                {
                    case (Options.ListSessions):
                        break;
                    case (Options.ListDiagnosers):
                        break;
                    case (Options.Help):
                        ShowUsage();
                        break;
                    case (Options.CollectLogs):
                    case (Options.Troubleshoot):
                    case (Options.CollectKillAnalyze):
                        {
                            CollectLogsAndTakeActions(option, args, ref argNum);
                            break;
                        }
                    default:
                        break;
                }

                Console.WriteLine();
            }
        }

        private static string GetToolToRun(string[] args, ref int argNum)
        {
            string diagnoserName = string.Empty;
            while (argNum < args.Length)
            {
                if (ArgumentIsAParameter(args[argNum]))
                {
                    // Done parsing all diagnosers
                    break;
                }

                if (int.TryParse(args[argNum], out _))
                {
                    // Done parsing all diagnosers, we've reached the timespan now
                    break;
                }

                diagnoserName = args[argNum];
                argNum++;
            }

            return GetV2ToolName(diagnoserName);
        }

        /// <summary>
        /// This function is needed because the diagnosers names have changed from DAAS V1 t
        /// DAAS V2 to ensure consistency with Linux App Service. We workaround this by still
        /// supporting old diagnoser names from DaasConsole so AutoHealing rules don't break
        /// </summary>
        /// <param name="diagnoserName"></param>
        /// <returns></returns>
        private static string GetV2ToolName(string diagnoserName)
        {
            string retval = diagnoserName;
            string toolNameToMatch = diagnoserName.ToLower();
            switch (toolNameToMatch)
            {
                case "memory dump":
                case "memorydump":
                    retval = "MemoryDump";
                    break;
                case "clr profiler":
                    retval = "Profiler";
                    break;
                case "clr profiler with thread stacks":
                    retval = "Profiler with Thread Stacks";
                    break;
                case "clr profiler cpustacks":
                    retval = "Profiler with CPU Stacks";
                    break;
            }

            return retval;
        }

        private static void CollectLogsAndTakeActions(Options options, string[] args, ref int argNum)
        {
            try
            {
                if (IsSessionOption(options))
                {
                    var toolName = GetToolToRun(args, ref argNum);
                    SubmitAndWaitForSession(toolName, string.Empty, options);
                    return;
                }
            }
            catch (Exception ex)
            {
                string logMessage = $"Unhandled exception in DaasConsole.exe - {ex} ";
                EventLog.WriteEntry("Application", logMessage, EventLogEntryType.Information);
                Console.WriteLine(logMessage);
                Logger.LogErrorEvent("Unhandled exception in DaasConsole.exe while collecting logs and taking actions", ex);
            }
        }

        private static bool IsSessionOption(Options options)
        {
            return options == Options.Troubleshoot
                || options == Options.CollectKillAnalyze
                || options == Options.CollectLogs;
        }

        private static void SubmitAndWaitForSession(string toolName, string toolParams, Options options)
        {
            Console.WriteLine($"Running Diagnosers on { Environment.MachineName}");

            var session = new Session()
            {
                Instances = new List<string>() { Environment.MachineName },
                Tool = toolName,
                ToolParams = toolParams,
                Mode = GetModeFromOptions(options),
                Description = GetSessionDescription()
            };

            //
            // do not await on this call. We just want to
            // submit and check the status in a loop
            //

            var sessionId = SessionManager.SubmitNewSessionAsync(session, invokedViaDaasConsole: true).Result;
            Console.WriteLine($"Session submitted for '{toolName}' with Id - {sessionId}");
            Console.Write("Waiting...");

            var details = new
            {
                Diagnoser = toolName,
                InstancesSelected = Environment.MachineName,
                Options = options.ToString()
            };

            var detailsString = JsonConvert.SerializeObject(details);
            Logger.LogDaasConsoleEvent("DaasConsole started a new Session", detailsString);
            EventLog.WriteEntry("Application", $"DaasConsole started with {detailsString} ", EventLogEntryType.Information);

            while (true)
            {
                Thread.Sleep(10000);
                Console.Write(".");
                var activeSession = SessionManager.GetActiveSessionAsync().Result;

                //
                // Either the session got completed, timed out
                // or error'ed, in either case, bail out
                // 
                if (activeSession == null)
                {
                    break;
                }

                //
                // The session is submitted but not picked up by any instance
                // so we should continue waiting...
                //
                if (activeSession.ActiveInstances == null)
                {
                    continue;
                }

                var currentInstance = activeSession.ActiveInstances
                    .FirstOrDefault(x => x.Name.Equals(
                        Environment.MachineName,
                        StringComparison.OrdinalIgnoreCase));
                if (currentInstance == null)
                {
                    //
                    // The current instance has not picked up the session
                    //
                    continue;
                }

                if (currentInstance.Status == Status.Analyzing)
                {
                    //
                    // Exit the loop once data has been collected and Analyzer
                    // has been started
                    
                    break;
                }
            }

            if (options == Options.CollectKillAnalyze)
            {
                Process mainSiteW3wpProcess = GetMainSiteW3wpProcess();
                Console.WriteLine($"Killing process {mainSiteW3wpProcess.ProcessName} with pid {mainSiteW3wpProcess.Id}");
                mainSiteW3wpProcess.Kill();
                Logger.LogSessionVerboseEvent($"DaasConsole killed process {mainSiteW3wpProcess.ProcessName} with pid {mainSiteW3wpProcess.Id}", sessionId);
            }
        }

        private static string GetSessionDescription()
        {
            //
            // Starting ANT98 AutoHealing will inject the environment variable
            // WEBSITE_AUTOHEAL_REASON whenever it is launching a custom action
            //

            string reason = "InvokedViaDaasConsole";
            string val = Environment.GetEnvironmentVariable("WEBSITE_AUTOHEAL_REASON");
            if (!string.IsNullOrWhiteSpace(val))
            {
                reason += $"-{val}";
            }

            return reason;
        }

        private static Mode GetModeFromOptions(Options options)
        {
            if (options == Options.CollectLogs)
            {
                return Mode.Collect;
            }
            else if (options == Options.CollectKillAnalyze || options == Options.Troubleshoot)
            {
                return Mode.CollectAndAnalyze;
            }

            throw new ArgumentException("Invalid diagnostic mode specified");
        }

        private static string GetBlobSasUriFromArg(string[] args, ref int argNum)
        {
            var sasUri = "";
            if (argNum > args.Length)
            {
                return sasUri;
            }
            else
            {
                try
                {
                    var sasUriString = args[argNum];
                    if (ArgumentIsAParameter(sasUriString))
                    {
                        var optionString = args[argNum].Substring(1, args[argNum].Length - 1);

                        if (optionString.StartsWith("blobsasuri:", StringComparison.OrdinalIgnoreCase))
                        {
                            sasUri = optionString.Substring("blobsasuri:".Length);
                            argNum++;
                        }
                    }
                }
                catch
                {
                }
            }

            return sasUri;
        }

        private static Process GetMainSiteW3wpProcess()
        {
            Console.WriteLine("Getting main site's w3wp process");

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
                Console.WriteLine("Woah, I missed it. Where did w3wp go?");
            }
            return mainSiteW3wpProcess;
        }

        private static TimeSpan GetTimeSpanFromArg(string[] args, ref int argNum)
        {
            int numberOfSeconds = 30;
            try
            {
                var numberOfSecondsStr = args[argNum];
                if (!ArgumentIsAParameter(numberOfSecondsStr))
                {
                    numberOfSeconds = int.Parse(numberOfSecondsStr);
                    argNum++;
                }
            }
            catch
            {
                // No timespan is specified. We'll default to 30 seconds;
            }

            return TimeSpan.FromSeconds(numberOfSeconds);
        }

        private static bool GetRunAllInstancesFromArg(string[] args, ref int argNum)
        {
            if (argNum > args.Length)
            {
                return false;
            }
            else
            {
                try
                {
                    var allInstancesString = args[argNum];
                    if (ArgumentIsAParameter(allInstancesString))
                    {
                        Options.TryParse(args[argNum].Substring(1, args[argNum].Length - 1), true, out Options option);
                        if (option == Options.AllInstances)
                        {
                            argNum++;
                            return true;
                        }

                    }
                }
                catch
                {
                    // Assume Options.AllInstances is false if not specified
                }
            }

            return false;
        }

        private static bool ArgumentIsAParameter(string currentArgument)
        {
            return currentArgument[0].Equals('-') || currentArgument[0].Equals('/');
        }

        private static void ShowUsage()
        {
            var optionDescriptions = new List<Argument>()
            {
                new Argument() {Command = Options.Troubleshoot, Usage = "<Diagnoser1> [<Diagnoser2> ...] [TimeSpanToRunForInSeconds] [-BlobSasUri:\"<A_VALID_BLOB_SAS_URI>\"] [-AllInstances]", Description = "Create a new Collect and Analyze session with the requested diagnosers. Default TimeSpanToRunForInSeconds is 30. If a valid BlobSasUri is specified, all data for the session will be stored on the specified blob account. By default this option collects data only on the current instance. To collect the data on all the instances specify -AllInstances."},
                new Argument() {Command = Options.CollectLogs, Usage = "<Diagnoser1> [<Diagnoser2> ...] [TimeSpanToRunForInSeconds] [-BlobSasUri:\"<A_VALID_BLOB_SAS_URI>\"] [-AllInstances]", Description = "Create a new Collect Only session with the requested diagnosers. Default TimeSpanToRunForInSeconds is 30. If a valid BlobSasUri is specified, all data for the session will be stored on the specified blob account. By default this option collects data only on the current instance. To collect the data on all the instances specify -AllInstances."},
                new Argument() {Command = Options.CollectKillAnalyze, Usage = "<Diagnoser1> [<Diagnoser2> ...] [TimeSpanToRunForInSeconds] [-BlobSasUri:\"<A_VALID_BLOB_SAS_URI>\"] [-AllInstances]", Description = "Create a new Collect Only session with the requested diagnosers, kill the main site's w3wp process to restart w3wp, then analyze the collected logs. Default TimeSpanToRunForInSeconds is 30. If a valid BlobSasUri is specified, all data for the session will be stored on the specified blob account. By default this option collects data only on the current instance. To collect the data on all the instances specify -AllInstances."},
                new Argument() {Command = Options.ListDiagnosers, Usage = "", Description = "List all available diagnosers"},
                new Argument() {Command = Options.ListSessions, Usage = "", Description = "List all sessions"},
                new Argument() {Command = Options.GetSasUri, Usage = "", Description = "Get the blob storage Sas Uri"},
                new Argument() {Command = Options.Setup, Usage = "", Description = "Start the continuous webjob runner (if it's already started this does nothing)"},
                new Argument() {Command = Options.GetSetting, Usage = "<SettingName>", Description = "The the value of the given setting"},
            };

            Console.WriteLine("\n Usage: DaasConsole.exe -<parameter1> [param1 args] [-parameter2 ...]\n");
            Console.WriteLine(" Parameters:\n");

            foreach (var option in optionDescriptions)
            {
                Console.WriteLine("   -{0} {1}", option.Command, option.Usage);
                Console.WriteLine("       {0}\n", option.Description);
            }

            Console.WriteLine(" Examples:");
            Console.WriteLine();
            Console.WriteLine("   To list all diagnosers run:");
            Console.WriteLine("       DaasConsole.exe -ListDiagnosers");
            Console.WriteLine();
            Console.WriteLine("   To collect and analyze memory dumps run");
            Console.WriteLine("       DaasConsole.exe -Troubleshoot \"Memory Dump\" 60");
            Console.WriteLine();
            Console.WriteLine("   To collect memory dumps, kill w3wp, and then analyze the logs run");
            Console.WriteLine("       DaasConsole.exe -CollectKillAnalyze \"Memory Dump\" 60 ");
            Console.WriteLine();
            Console.WriteLine("To specify a custom folder to get the diagnostic tools from, set the DiagnosticToolsPath setting to the desired location");
        }
    }
}
