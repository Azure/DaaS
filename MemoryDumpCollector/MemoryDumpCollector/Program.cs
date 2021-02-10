//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

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
            Mode,
            ProcessName,
            OutputDir,
            Child,
            CdbDir,
            Exception,
            Help
        }

        private static string _processName = null;
        private static string _mode = string.Empty;
        private static string _exceptionCode = null;
        private static string _outputDir = null;
        private static bool _includeChildProcesses = false;
        private static string _cdbFolder;
        //used for crash and exception mode monitoring (not Hang mode)
        private static bool _readyToExit = false;
        private static bool _writingDump = false;
        //keep track of target processes in crash or exception monitoring
        private static List<Process> _TargetProcesses = new List<Process>();
        //limit the number of crash mode debuggers we launch on each worker
        private static int _maxDebuggers = 10;
        private static List<int> _processesToIgnore = new List<int>();
        private static readonly string[] _additionalProcesses = new string[] { "java" };

        static void Main(string[] args)
        {
            try
            {
                ParseInputs(args);

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

                if (_mode.Equals("crash", StringComparison.OrdinalIgnoreCase))
                {
                    // For crash mode lets ignore all cmd processes since they're not going to provide us with interesting dumps 
                    // (less clutter for the user that way)
                    // In crash mode we will initially monitor PHP processes for crashes. May reconsider / throttle further based on perf tests
                    _processesToIgnore.AddRange(
                                Process.GetProcesses()
                                .Where(p => p.ProcessName.Equals("cmd", StringComparison.OrdinalIgnoreCase))
                                .Select(p => p.Id));

                }
                else
                {
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
                                || p.ProcessName.Equals("procdump", StringComparison.OrdinalIgnoreCase)
                                || p.ProcessName.Equals("procdump64", StringComparison.OrdinalIgnoreCase)
                                || p.ProcessName.Equals("crashmon", StringComparison.OrdinalIgnoreCase)
                                || p.ProcessName.Equals("dbghost", StringComparison.OrdinalIgnoreCase)
                                )
                                .Select(p => p.Id));
                }

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

                    foreach (var process in childProcesses)
                    {
                        Console.WriteLine("Child process found: " + process.ProcessName + " PID: " + process.Id.ToString());
                        processesToDump[process.Id] = process;
                    }
                }
                //crash = named exception / code or unhandled 2nd chance
                if (_mode.Equals("crash", StringComparison.OrdinalIgnoreCase))
                {
                    //check for named exception / code monitoring
                    if (_exceptionCode != null)
                    {
                        Console.WriteLine("Will monitor the first " + _maxDebuggers.ToString() + " of the following processes:");
                        foreach (var p in processesToDump.Values)
                        {

                            Console.WriteLine("  " + p.ProcessName + " - Id: " + p.Id);
                            if (_TargetProcesses.Count < _maxDebuggers)
                            {
                                AttachFirstChanceException(p, _outputDir, _exceptionCode);
                            }
                        }
                    }
                    //watch for 2nd chance exception
                    else
                    {
                        Console.WriteLine("Will monitor the first " + _maxDebuggers.ToString() + " of the following processes:");
                        foreach (var p in processesToDump.Values)
                        {
                            Console.WriteLine("  " + p.ProcessName + " - Id: " + p.Id);
                            if (_TargetProcesses.Count < _maxDebuggers)
                            {
                                AttachCrashProcDump(p, _outputDir);
                            }
                        }
                    }

                    //returns when a dump is generated or all targets have exited
                    WatchandWaitForDumps();

                    //detach remaining debug sessions gracefully
                    DetachCrashProcDump();
                    Console.WriteLine("memoryDumpCollector finished and exiting");

                }
                else
                {
                    //Hang dump mode
                    List<string> processList = new List<string>();

                    Console.WriteLine("Will take dump of following processes:");
                    Logger.LogDiagnoserEvent("Will take dump of following processes:");
                    foreach (var p in processesToDump.Values)
                    {
                        Console.WriteLine("  " + p.ProcessName + " - Id: " + p.Id);
                        processList.Add($"{p.ProcessName}({p.Id})");
                        Logger.LogDiagnoserEvent("  " + p.ProcessName + " - Id: " + p.Id);
                    }

                    Logger.LogStatus($"Collecting dumps of processes - { string.Join(",", processList) }");
                    foreach (var p in processesToDump.Values)
                    {
                        GetMemoryDumpProcDump(p, _outputDir);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Un-handled exception in MemoryDumpCollector", ex);
            }
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
                    case (Options.Mode):
                        {
                            i++;
                            _mode = args[i];
                            break;
                        }
                    case (Options.Exception):
                        {
                            i++;
                            _exceptionCode = args[i];
                            break;
                        }
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
            string command = Path.Combine(_cdbFolder, "procdump.exe");
            string arguments = " -accepteula -r -ma {0} {1}\\{2}_{3}_{0}.dmp";
            MemoryStream outputStream = null;
            MemoryStream errorStream = null;

            try
            {
                var cancellationTokenSource = new CancellationTokenSource();
                Console.WriteLine(process.ProcessName + " is " + (process.IsWin64() ? "64" : "32") + "-bit");
                arguments = string.Format(arguments, process.Id, outputDir, Environment.MachineName, process.ProcessName);
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
                
                outputStream = new MemoryStream();
                errorStream = new MemoryStream();
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
                    Logger.TraceFatal($"ProcDump failed to run. Detailed ProcDump output: {Environment.NewLine} {procDumpOutput}");
                }

                
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Failed in GetMemoryDumpProcDump for arguments:{arguments}", ex);
            }
        }

        //Crash Mode dump generation and logs 1st chance exceptions
        private static void AttachCrashProcDump(Process targetProcess, string outputDir)
        {
            //debugger to attach
            string command = Path.Combine(_cdbFolder, "procdump.exe");
            //launch via cmd to redirect procdump output to unique crash log
            string arguments = " -accepteula -ma -e 1 -f \"\" {0} \"{1}\\{2}_{0}.dmp\" >>\"{1}\\{2}_{0}_Crash.log\"";
            arguments = string.Format(arguments, targetProcess.Id, outputDir, targetProcess.ProcessName);

            Console.WriteLine("Target: " + targetProcess.ProcessName + " is " + (targetProcess.IsWin64() ? "64" : "32") + "-bit");
            Console.WriteLine("Command: " + command + " " + arguments);
            _TargetProcesses.Add(targetProcess);

            //CMD is flakey with spaces in path
            //need to have double quotes around the entire argument in addition to quotes around the paths with spaces
            string cmdArgs = "/c \"\"" + command + "\"" + arguments + "\"";

            var crashWatch = new Process()
            {
                StartInfo =
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    //WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                }
            };
            crashWatch.Start();
        }

        private static void DetachCrashProcDump()
        {
            //Signal all the procdumps we launched to detatch from targets
            Console.WriteLine("Signaling all crash debuggers in list. Total count: " + _TargetProcesses.Count.ToString());
            foreach (Process p in _TargetProcesses)
            {
                Console.WriteLine("Detaching from target  " + p.ProcessName + " - Id: " + p.Id);
                SignalProcDumpDetach(p.Id);
            }
        }

        private static bool SignalProcDumpDetach(int targetPid)
        {
            string ewhName = "Procdump-" + targetPid.ToString();
            EventWaitHandle ewh = null;

            // Attempt to open and signal the named event to shut down the debugger Ex: Procdump-5555
            try
            {
                ewh = EventWaitHandle.OpenExisting(ewhName);
                ewh.Set();
                Console.WriteLine("Named event " + ewhName + " signaled");
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Console.WriteLine("Named event " + ewhName + " does not exist.");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Unauthorized access: {0}", ex.Message);
                return false;
            }
            //if we didn't catch anything assume success or target process exited
            return true;
        }
        //Dump on matching 1st chance exception
        private static void AttachFirstChanceException(Process targetProcess, string outputDir, string exceptionCode)
        {
            //debugger to attach
            string command = Path.Combine(_cdbFolder, "procdump.exe");

            //launch via cmd to redirect to unique crash log
            string arguments = " -accepteula -ma -e 1 -f \"{3}\" {0} \"{1}\\{2}_{3}_{0}.dmp\" >>\"{1}\\{2}_{3}_{0}_Crash.log\"";
            arguments = string.Format(arguments, targetProcess.Id, outputDir, targetProcess.ProcessName, exceptionCode);

            Console.WriteLine("Target: " + targetProcess.ProcessName + " is " + (targetProcess.IsWin64() ? "64" : "32") + "-bit");
            Console.WriteLine("Command: " + command + " " + arguments);
            _TargetProcesses.Add(targetProcess);

            //CMD is flakey with spaces in path
            //need to have double quotes around the entire argument in addition to quotes around the paths with spaces
            string cmdArgs = "/c \"\"" + command + "\"" + arguments + "\"";

            var crashWatch = new Process()
            {
                StartInfo =
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    //WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                }
            };
            crashWatch.Start();
        }

        //Waiting around until the targets are gone or
        //the dump file has finished writing
        private static void WatchandWaitForDumps()
        {
            FileSystemWatcher watcher = new FileSystemWatcher
            {
                Path = _outputDir,
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.dmp"
            };

            // Add event handlers.
            watcher.Created += new FileSystemEventHandler(OnChanged);
            watcher.Renamed += new RenamedEventHandler(OnRenamed);

            // Begin watching.
            watcher.EnableRaisingEvents = true;
            //Need to stay alive until a dump is captured or targets stopped
            Console.WriteLine("Waiting and watching for a .dmp file in " + _outputDir);
            //_readyToExit signaled by event handler once dump has completed
            while (!_readyToExit)
            {
                Thread.Sleep(System.TimeSpan.FromSeconds(15));
                if (!TargetsAlive())
                {
                    //exit as no targets running
                    Console.WriteLine("No targets remaining, exiting.");
                    _readyToExit = true;
                }
            }
        }

        //Verify targets still exist
        private static bool TargetsAlive()
        {
            //Checking for active targets.
            if (_writingDump)
            {
                //dump is being generated
                return true;
            }
            int i = 0;
            foreach (Process p in _TargetProcesses)
            {
                if (!p.HasExited)
                {
                    i++;
                }
            }
            if (i == 0)
            {
                return false;
            }
            return true;
        }

        //Dump file creation event handlers.
        private static void OnChanged(object source, FileSystemEventArgs e)
        {
            // dumpfile created
            _writingDump = true;
            Console.WriteLine("File found: " + e.FullPath + " " + e.ChangeType);
            FileInfo dmpInfo = new FileInfo(e.FullPath);
            Console.WriteLine("Waiting for dump to finish writing.");
            //fileinfo.Length cannot be used as data is flushed periodically
            while (IsFileInUse(e.FullPath) == true)
            {
                //wait for dump write operation to finish
                Thread.Sleep(System.TimeSpan.FromSeconds(10));

            }
            Console.WriteLine("File size: " + dmpInfo.Length.ToString());
            _writingDump = false;
            _readyToExit = true;

        }
        private static void OnRenamed(object source, RenamedEventArgs e)
        {
            // file renamed to a dumpfile
            Console.WriteLine("File found: {0} renamed to {1}", e.OldFullPath, e.FullPath);
            _readyToExit = true;
        }

        private static bool IsFileInUse(string filePath)
        {
            FileStream fStream = null;
            try
            {
                fStream = File.OpenWrite(filePath);

            }
            catch (IOException)
            {
                return true;
            }
            finally
            {
                if (fStream != null)
                    fStream.Close();

            }
            return false;
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
