// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DaaS;

namespace MemoryDumpCollector
{
    class Program
    {
        enum Options
        {
            ProcessName,
            OutputDir,
            Child,
            CdbDir,
            Help
        }

        private const int MaxProcessCountToDump = 5;
        private static string _processName = null;
        private static string _outputDir = null;
        private static bool _includeChildProcesses = false;
        private static string _cdbFolder;

        private static List<int> _processesToIgnore = new List<int>();
        private static readonly string[] _additionalProcesses = new string[] { "java" };

        static void Main(string[] args)
        {
            try
            {
                ParseInputs(args);

                if (!IsScmSeparationDisabled())
                {
                    // Determine which processes this process is a child of (since we don't want to call a memory dump on ourselves)
                    Process parentProcess = Process.GetCurrentProcess();
                    while (parentProcess != null)
                    {
                        if (parentProcess.ProcessName.Equals(_processName, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Ignoring parent " + parentProcess.GetPrintableInfo());
                            // Don't collect memory dumps of our parent process. Taking the dump can sometimes freeze cdb and the process it's take a dump of
                            _processesToIgnore.Add(parentProcess.Id);
                        }
                        parentProcess = parentProcess.GetParentProcess();
                    }

                    // We don't want to collect memory dumps of ourself
                    _processesToIgnore.AddRange(Process.GetCurrentProcess().GetAllChildProcesses().Select(p => p.Id).ToList());
                    _processesToIgnore.Add(Process.GetCurrentProcess().Id);
                }

                // We don't want to collect processes spawned by the runner either
                Process runnerProcesses = Process.GetProcessesByName("DaaSRunner").FirstOrDefault();
                if (runnerProcesses != null)
                {
                    AddtoIgnoredProcessList(runnerProcesses);
                }

                Process consoleProcesses = Process.GetProcessesByName("DaaSConsole").FirstOrDefault();
                if (consoleProcesses != null)
                {
                    AddtoIgnoredProcessList(consoleProcesses);
                }

                // Lets also ignore all cmd processes since they're not going to provide us with interesting dumps 
                // (less clutter for the user that way)
                // We'll also ignore all php_cgi processes since the PHP analyze will analyze the process directly.
                _processesToIgnore.AddRange(
                            Process.GetProcesses()
                            .Where(p => p.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("php-cgi", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("workerforwarder", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("msvsmon", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("SnapshotHolder_x86", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("SnapshotHolder_x64", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("DaasConsole", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("SnapshotUploader64", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("SnapshotUploader86", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("SnapshotUploader", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("SnapshotHolder", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("procdump", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("procdump64", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("crashmon", StringComparison.OrdinalIgnoreCase)
                            || p.ProcessName.Equals("dbghost", StringComparison.OrdinalIgnoreCase)
                            )
                            .Select(p => p.Id));


                List<Process> requestedProcesses =
                    Process.GetProcesses().Where(p => p.ProcessName.Equals(_processName, StringComparison.OrdinalIgnoreCase)
                        && !_processesToIgnore.Contains(p.Id)).ToList();

                var additionalProcesses = Process.GetProcesses().Where(p => _additionalProcesses.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase));
                requestedProcesses = requestedProcesses.Union(additionalProcesses).ToList();

                if (requestedProcesses.Count == 0)
                {
                    Console.WriteLine("No matching processes found");
                    Logger.LogDiagnoserEvent("No matching processes found");
                    return;
                }

                // Using a dictionary helps prevent processes from being dumped twice
                var processesToDump = new Dictionary<int, Process>();
                foreach (var process in requestedProcesses)
                {
                    processesToDump[process.Id] = process;
                }

                if (_includeChildProcesses)
                {
                    Console.WriteLine("Including any child processes");
                    // Include all child processes except for the current process and its children
                    var childProcesses =
                        requestedProcesses.SelectMany(p => p.GetAllChildProcesses())
                            .Where(p => !_processesToIgnore.Contains(p.Id)).ToList();

                    //
                    // Explicitly look for dotnet.exe first to ensure we
                    // don't miss it while looping through other child processes
                    //

                    foreach (var process in childProcesses.Where(p => p.ProcessName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)))
                    {
                        Console.WriteLine("Child process found: " + process.ProcessName + " PID: " + process.Id.ToString());
                        processesToDump[process.Id] = process;
                    }

                    //
                    // Now loop the rest of the child processes.
                    //

                    foreach (var process in childProcesses)
                    {
                        Console.WriteLine("Child process found: " + process.ProcessName + " PID: " + process.Id.ToString());
                        processesToDump[process.Id] = process;

                        //
                        // Make sure we don't dump more than MaxProcessCountToDump
                        //

                        if (processesToDump.Count >= MaxProcessCountToDump)
                        {
                            break;
                        }
                    }
                }

                List<string> processList = new List<string>();

                Console.WriteLine("Will take dump of following processes:");
                Logger.LogDiagnoserEvent("Will take dump of following processes:");

                int mainSiteWorkerProcessId = -1;
                foreach (var p in processesToDump.Values)
                {
                    Console.WriteLine("  " + p.ProcessName + " - Id: " + p.Id);
                    processList.Add($"{p.ProcessName}({p.Id})");
                    Logger.LogDiagnoserEvent("  " + p.ProcessName + " - Id: " + p.Id);

                    if (p.ProcessName.Equals("w3wp", StringComparison.OrdinalIgnoreCase))
                    {
                        mainSiteWorkerProcessId = p.Id;
                    }
                }

                Logger.LogStatus($"Collecting dumps of processes - { string.Join(",", processList) }");
                foreach (var p in processesToDump.Values)
                {
                    GetMemoryDumpProcDump(p, _outputDir);
                }

                LaunchStackTracer(mainSiteWorkerProcessId);

            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Un-handled exception in MemoryDumpCollector", ex);
                Console.WriteLine($"Un-handled exception in MemoryDumpCollector {ex}");
            }
        }

        private static bool IsScmSeparationDisabled()
        {
            return Utilities.GetAppSettingAsBoolOrDefault("WEBSITE_DISABLE_SCM_SEPARATION", false);
        }

        private static void LaunchStackTracer(int mainSiteWorkerProcessId)
        {
            if (mainSiteWorkerProcessId == -1)
            {
                Logger.LogDiagnoserWarningEvent("Failed to determine main site worker process", new ApplicationException("w3wp process for the main site not found"));
                return;
            }

            string stackTracerCmd = Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\IIS\Microsoft Web Hosting Framework\DWASMod\stacktracer\stacktracer.exe");
            string stackTracerArgs = $"-p:{mainSiteWorkerProcessId} -r:ManualDumpCollection -c:true";
            Logger.LogDiagnoserEvent($"Launching process {stackTracerCmd} {stackTracerArgs}");

            var stackTracer = new Process
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = stackTracerCmd,
                    Arguments = stackTracerArgs,
                    UseShellExecute = false
                }
            };

            stackTracer.Start();
            stackTracer.WaitForExit();

            Logger.LogDiagnoserEvent("StackTracer completed");
        }

        private static void AddtoIgnoredProcessList(Process process)
        {
            _processesToIgnore.Add(process.Id);
            _processesToIgnore.AddRange(process.GetAllChildProcesses().Select(p => p.Id));
        }

        private static void ParseInputs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                Options option;
                if (!(args[i][0] == '-' || args[i][0] == '/') ||
                    !Enum.TryParse(args[i].Substring(1, args[i].Length - 1), true, out option))
                {
                    Console.WriteLine("Unrecognized option specified: " + args[i]);
                    Logger.LogDiagnostic("Unrecognized option specified: " + args[i]);
                    ShowUsage();
                    return;
                }

                switch (option)
                {
                    case (Options.ProcessName):
                        {
                            i++;
                            _processName = args[i];
                            break;
                        }
                    case (Options.OutputDir):
                        {
                            i++;
                            _outputDir = args[i];
                            Logger.Init("", _outputDir, "MemoryDumpCollector", true);
                            break;
                        }
                    case (Options.Child):
                        {
                            _includeChildProcesses = true;
                            break;
                        }
                    case (Options.CdbDir):
                        {
                            i++;
                            _cdbFolder = args[i];
                            break;
                        }
                    default:
                        {
                            Console.WriteLine("Unrecognized option specified: " + args[i]);
                            ShowUsage();
                            break;
                        }
                }
            }

            if (_processName == null)
            {
                Console.WriteLine("Must specify process name");
                ShowUsage();
            }

            if (_cdbFolder == null)
            {
                Console.WriteLine("Must specify the cdb folder");
                ShowUsage();
            }

            if (_outputDir == null)
            {
                Console.WriteLine("Must specify an output folder");
            }
        }

        private static void GetMemoryDumpProcDump(Process process, string outputDir)
        {
            string command = EnvironmentVariables.ProcdumpPath;
            try
            {
                string debuggerComment = "DaaS";
                var sessionDescription = Environment.GetEnvironmentVariable("DAAS_SESSION_DESCRIPTION");
                if (!string.IsNullOrWhiteSpace(sessionDescription))
                {
                    debuggerComment = $"\"{ sessionDescription }\"";
                }

                var cancellationTokenSource = new CancellationTokenSource();
                Console.WriteLine(process.ProcessName + " is " + (process.IsWin64() ? "64" : "32") + "-bit");
                string arguments = $" -accepteula -r -dc {debuggerComment} -ma {process.Id} {outputDir}\\{Environment.MachineName}_{process.ProcessName}_{process.Id}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.dmp";
                Console.WriteLine("Comand:");
                Console.WriteLine(command + " " + arguments);

                Logger.LogDiagnoserVerboseEvent($"Launching Command : {command} {arguments}");

                var dumpGenerator = new Process()
                {
                    StartInfo =
                {
                    FileName = command,
                    Arguments = arguments,
                    //WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                }
                };

                MemoryStream outputStream = new MemoryStream();
                MemoryStream errorStream = new MemoryStream();
                dumpGenerator.Start();

                var tasks = new List<Task>
                {
                    MemoryStreamExtensions.CopyStreamAsync(dumpGenerator.StandardOutput.BaseStream, outputStream, cancellationTokenSource.Token),
                    MemoryStreamExtensions.CopyStreamAsync(dumpGenerator.StandardError.BaseStream, errorStream, cancellationTokenSource.Token)
                };

                string processMemoryConsumption = string.Empty;
                long processPrivateBytes = 0;
                try
                {
                    processPrivateBytes = process.PrivateMemorySize64;
                    processMemoryConsumption = $"{ process.ProcessName} Private Bytes consumption is { ConversionUtils.BytesToString(processPrivateBytes) }";
                }
                catch (Exception)
                {
                }

                long tenGb = (long)10 * 1024 * 1024 * 1024;
                if (processPrivateBytes > tenGb)
                {
                    string message = $"{processMemoryConsumption}. Cancelling dump generation.";
                    Logger.TraceFatal(message, true);
                    Environment.Exit(0);
                }

                Logger.LogDiagnoserVerboseEvent($"Dump Generator started, waiting for it to exit - {processMemoryConsumption}");

                // Keep this process alive untile the dump has been collected
                int sleepCount = 0;
                while (!dumpGenerator.HasExited)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                    ++sleepCount;
                    if (sleepCount == 30)
                    {
                        sleepCount = 0;
                        Logger.LogDiagnoserVerboseEvent($"Waiting for Dump Generator invoked with args ({arguments}) to finish");
                    }
                }

                Task.WhenAll(tasks);

                string output = outputStream.ReadToEnd();
                string error = errorStream.ReadToEnd();
                string procDumpOutput = output;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    procDumpOutput += Environment.NewLine + "Error Stream Output" + Environment.NewLine + "-----------------" + Environment.NewLine + error;
                }
                Logger.LogDiagnoserVerboseEvent(procDumpOutput);

                if (output.ToLower().Contains("Dump 1 complete".ToLower()))
                {
                    Logger.LogDiagnoserVerboseEvent($"Procdump completed successfully.  Detailed ProcDump output: {Environment.NewLine} {procDumpOutput}");
                    Logger.LogStatus($"Collected dump of {process.ProcessName} ({ process.Id })");
                }
                else
                {
                    procDumpOutput = CleanupProcdumpOutput(procDumpOutput);
                    Logger.TraceFatal($"ProcDump failed to run. Detailed ProcDump output: {Environment.NewLine} {procDumpOutput}");
                }


            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Failed in GetMemoryDumpProcDump", ex);
            }
        }

        private static string CleanupProcdumpOutput(string procDumpOutput)
        {
            var output = new List<string>();
            foreach (var line in procDumpOutput.Split(Environment.NewLine.ToCharArray()))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("ProcDump v")
                    || line.StartsWith("Copyright") || line.StartsWith("Sysinternals")
                    || line.StartsWith("ProcDump failed to run") || line.Contains("Waiting for dump to complete")
                    || line.Contains("Dump count reached") || line.Contains("Dump 1 initiated")
                    )
                {
                    continue;
                }
                else
                {
                    output.Add(line);
                }
            }

            if (output.Count() > 0)
            {
                return string.Join(Environment.NewLine, output);
            }

            return procDumpOutput;
        }

        private static void ShowUsage()
        {
            Console.WriteLine("Usage:");
            foreach (var option in Enum.GetNames(typeof(Options)))
            {
                Console.WriteLine(" -" + option);
            }

            Environment.Exit(-1);
        }
    }
}
