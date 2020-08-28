//-----------------------------------------------------------------------
// <copyright file="CpuMonitoring.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Threading;
using DaaS.Storage;
using DaaS.Leases;
using Microsoft.WindowsAzure.Storage;
using System.Threading.Tasks;

namespace DaaS
{
    public class CpuMonitoring
    {
        static ConcurrentDictionary<int, MonitoredProcess> ProcessList;
        static readonly StringBuilder _logger = new StringBuilder();

        private int _loggerCount = 0;
        const int MAX_LINES_IN_LOGFILE = 40 * 1000;
        public double CpuUsageLastMinute
        {
            get;
            private set;
        }

        static List<string> _processesToMonitor = new List<string>();
        static string _sessionId = string.Empty;

        static readonly string[] _processesAlwaysExcluded = new string[] { "daasrunner", "daasconsole", "workerforwarder" ,
                                                                            "msvsmon", "cmd", "powershell", "SnapshotHolder_x64",
                                                                            "SnapshotHolder_x86", "ApplicationInsightsProfiler",
                                                                            "VSDiagnostics", "clrprofilingcollector", "clrprofilinganalyzer",
                                                                            "stacktracer32","stacktracer64", "jstackparser", "logparser",
                                                                            "procdump", "procdump64", "cdb","loganalysisworker", "PhpReportGen",
                                                                            "DumpAnalyzer", "MemoryDumpCollector", "SnapshotUploader64", 
                                                                            "SnapshotUploader86", "crashmon"};
        public void InitializeMonitoring(MonitoringSession session)
        {
            if (session != null)
            {
                if (session.ProcessesToMonitor != null)
                {
                    _processesToMonitor = session.ProcessesToMonitor.Split(',').ToList();
                }
                _sessionId = session.SessionId;

                var sessionDetails = JsonConvert.SerializeObject(session, Formatting.None);
                AppendToMonitoringLog($"Monitoring started [CPU={session.CpuThreshold}%, Mode={session.Mode}, MaxActions={session.MaxActions}, Threshold={session.ThresholdSeconds}s, CheckEvery={session.MonitorDuration}s]", true);
            }

            ProcessList = new ConcurrentDictionary<int, MonitoredProcess>();
        }

        private TimeSpan GetProcessCpuTime(int pid)
        {
            Process p = null;
            try
            {
                p = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
            }

            if (p == null)
            {
                Trace.TraceInformation($"VERBOSE: p is null");
                return TimeSpan.MinValue;
            }
            return p.TotalProcessorTime;
        }

        public bool MonitorCpu(MonitoringSession session)
        {
            int cpuThreshold = session.CpuThreshold;
            int seconds = session.ThresholdSeconds;
            int monitorDuration = session.MonitorDuration;
            string actionToExecute = session.ActionToExecute;
            string argumentsToAction = session.ArgumentsToAction;
            int maxActions = session.MaxActions == 0 ? int.MaxValue : session.MaxActions;
            bool monitorScmProcesses = session.MonitorScmProcesses;
            string blobSasUri = session.BlobSasUri;

            if (string.IsNullOrWhiteSpace(actionToExecute))
            {
                actionToExecute = @"D:\devtools\sysinternals\procdump.exe";
            }
            if (string.IsNullOrWhiteSpace(argumentsToAction))
            {
                argumentsToAction = " -accepteula -ma {PROCESSID} {OUTPUTPATH}";
            }

            foreach (var process in Process.GetProcesses())
            {
                if (_processesToMonitor.Count > 0)
                {
                    if (!_processesToMonitor.Any(s => s.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }

                if (!monitorScmProcesses)
                {
                    var envVar = Utilities.GetEnvironmentVariablesCore(process.Handle);
                    if (Utilities.GetIsScmSite(envVar))
                    {
                        continue;
                    }
                }

                if (_processesAlwaysExcluded.Any(s => s.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                int id = process.Id;
                if (!ProcessList.ContainsKey(id))
                {
                    var cpuTime = GetProcessCpuTime(id);
                    MonitoredProcess p = new MonitoredProcess
                    {
                        CPUTimeStart = cpuTime,
                        Name = Process.GetProcessById(id).ProcessName,
                        CPUTimeCurrent = cpuTime,
                        LastMonitorTime = DateTime.UtcNow,
                        ThresholdExeededCount = 0
                    };
                    bool booProcessAdded = ProcessList.TryAdd(id, p);
                    if (booProcessAdded)
                    {
                        AppendToMonitoringLog($"Added process {process.ProcessName}({id}) to monitoring", true);
                    }
                }
            }

            var processesMonitored = new List<string>();
            foreach (var id in ProcessList.Keys)
            {
                var start = ProcessList[id].CPUTimeStart;
                var oldCPUTime = ProcessList[id].CPUTimeCurrent;
                var processCpuTime = GetProcessCpuTime(id);

                if (processCpuTime != TimeSpan.MinValue)
                {
                    TimeSpan newCPUTime = processCpuTime - start;
                    var cpuTimeSeconds = (newCPUTime - oldCPUTime).TotalSeconds;
                    var durationSeconds = DateTime.UtcNow.Subtract(ProcessList[id].LastMonitorTime).TotalSeconds;

                    // for the first time CPU Time will be 
                    // negative as startTime is not subtracted
                    if (cpuTimeSeconds < 0)
                    {
                        CpuUsageLastMinute = 0;
                    }
                    else
                    {
                        CpuUsageLastMinute = cpuTimeSeconds / (Environment.ProcessorCount * durationSeconds);
                    }

                    ProcessList[id].CPUTimeCurrent = newCPUTime;
                    ProcessList[id].LastMonitorTime = DateTime.UtcNow; ;
                    var cpuPercent = CpuUsageLastMinute * 100;

                    processesMonitored.Add($"{ProcessList[id].Name}({id}):{cpuPercent.ToString("0")} %");


                    var actionsExecuted = GetTotalCustomActionsExecuted(session.SessionId);

                    bool terminateMonitoring = false;
                    if (actionsExecuted >= maxActions)
                    {
                        AppendToMonitoringLog("Maximum number of actions configured on all instances have executed so terminating shortly!", true);
                        terminateMonitoring = true;
                    }

                    if (DateTime.UtcNow.Subtract(session.StartDate).TotalHours >= session.MaximumNumberOfHours)
                    {
                        AppendToMonitoringLog("Maximum time limit for this session has reached so terminating shortly!", true);
                        terminateMonitoring = true;
                    }
                    
                    if (terminateMonitoring)
                    {
                        var dumpsCollected = GetTotalCustomActionsCompleted(session.SessionId);
                        int sleepCount = 0;
                        AppendToMonitoringLog("Waiting for all instances to collect and move the dumps", true);
                        while (dumpsCollected < actionsExecuted && sleepCount < 20)
                        {
                            AppendToMonitoringLog($"Total actions executed = {actionsExecuted} and dumps moved = {dumpsCollected}, waiting for the rest to finish", true);
                            Thread.Sleep(15000);
                            ++sleepCount;
                            actionsExecuted = GetTotalCustomActionsExecuted(session.SessionId);
                            dumpsCollected = GetTotalCustomActionsCompleted(session.SessionId);
                        }
                        AppendToMonitoringLog("All instances finsihed collecting data so terminating", true);
                        return true;
                    }

                    if (cpuPercent >= cpuThreshold)
                    {
                        AppendToMonitoringLog($"{ProcessList[id].Name}({id}) CPU:{cpuPercent.ToString("0.00")} %");
                        int thresholdCount = ++ProcessList[id].ThresholdExeededCount;
                        int currentCpuConsumptionWithTime = thresholdCount * monitorDuration;
                        if (currentCpuConsumptionWithTime >= seconds)
                        {
                            actionsExecuted = GetTotalCustomActionsExecuted(session.SessionId);

                            string fileName = Environment.MachineName + "_" + ProcessList[id].Name + "_" + id + "_" + DateTime.Now.Ticks.ToString();
                            string customActionFile = fileName + ".customaction";
                            
                            string outputPath = MonitoringSessionController.TempFilePath;
                            FileSystemHelpers.CreateDirectoryIfNotExists(outputPath);
                            string dumpFileInTempDirectory = Path.Combine(outputPath, fileName + ".dmp");

                            if (session.Mode != SessionMode.Kill)
                            {
                                AppendToMonitoringLog($"Actions Executed on all instances = {actionsExecuted} of {maxActions}", true);
                            }

                            if (ShouldCollectData(session.Mode))
                            {
                                CreateCustomActionFile(session.SessionId, customActionFile);                                
                                ExecuteAction(dumpFileInTempDirectory, id, actionToExecute, argumentsToAction);
                            }

                            if (ShouldKillProcess(session.Mode))
                            {
                                KillProcessConsumingCpu(id, session.SessionId);
                            }

                            if (ShouldCollectData(session.Mode))
                            {
                                MoveToPermanentStorage(session.SessionId, dumpFileInTempDirectory, fileName + ".dmp", blobSasUri);
                                customActionFile = fileName + ".customactioncompleted";
                                CreateCustomActionFile(session.SessionId, customActionFile);


                                // Since we copied the file to permanent storage, delete the time file if the mode 
                                // doesnt require analysis to be done or if the number of instances is more than 1
                                if (session.Mode != SessionMode.CollectKillAndAnalyze || HeartBeats.HeartBeatController.GetNumberOfLiveInstances() > 1)
                                {
                                    FileSystemHelpers.DeleteFileSafe(dumpFileInTempDirectory);
                                }
                            }
                                
                            actionsExecuted = GetTotalCustomActionsExecuted(session.SessionId);
                            ProcessList[id].ThresholdExeededCount = 0;
                            if (actionsExecuted >= maxActions)
                            {
                                AppendToMonitoringLog("Max number of actions configured for this session have executed so terminating shortly!", true);
                                Thread.Sleep(5000);
                                return true;
                            }
                        }
                        else
                        {
                            AppendToMonitoringLog($"CPU Percent {cpuPercent.ToString("0.00")} % > [{ cpuThreshold } %] for {currentCpuConsumptionWithTime} seconds for { ProcessList[id].Name} ({ id }), waiting to reach threshold of {seconds} seconds", true);
                        }
                    }
                    else
                    {
                        if (ProcessList[id].ThresholdExeededCount > 0)
                        {
                            AppendToMonitoringLog("Resetting CPU threshold", true);
                            ProcessList[id].ThresholdExeededCount = 0;
                        }
                    }
                }
                else
                {
                    Trace.TraceInformation($"VERBOSE: processCpuTime == TimeSpan.MinValue ");
                }
            }

            AppendToMonitoringLog(string.Join(", ", processesMonitored));
            RemoveOldProcessesFromMonitoringList(ProcessList);

            return false;
        }

        private int GetTotalCustomActionsExecuted(string sessionId)
        {
            var sessionDirectory = GetLogsFolderForSession(sessionId);
            var actionsExecuted = FileSystemHelpers.GetFilesInDirectory(sessionDirectory, "*.customaction", false, SearchOption.TopDirectoryOnly).Count;
            return actionsExecuted;
        }

        private int GetTotalCustomActionsCompleted(string sessionId)
        {
            var sessionDirectory = GetLogsFolderForSession(sessionId);
            var dumpCount = FileSystemHelpers.GetFilesInDirectory(sessionDirectory, "*.customactioncompleted", false, SearchOption.TopDirectoryOnly).Count;
            return dumpCount;
        }

        private bool ShouldCollectData(SessionMode mode)
        {
            return mode == SessionMode.CollectAndKill || mode == SessionMode.Collect || mode == SessionMode.CollectKillAndAnalyze;
        }

        private bool ShouldKillProcess(SessionMode mode)
        {
            return mode == SessionMode.CollectAndKill || mode == SessionMode.CollectKillAndAnalyze || mode == SessionMode.Kill;
        }

        public static void CleanRemainingLogsIfAny()
        {
            string cpuMonitorPath = MonitoringSessionController.GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            if (FileSystemHelpers.DirectoryExists(cpuMonitorPath))
            {
                foreach (var log in FileSystemHelpers.GetFilesInDirectory(cpuMonitorPath, "*.log", false, SearchOption.TopDirectoryOnly))
                {
                    FileSystemHelpers.DeleteFileSafe(log);
                }
            }
        }

        private void KillProcessConsumingCpu(int id, string sessionId)
        {
            try
            {
                var p = Process.GetProcessById(id);
                if (p != null)
                {
                    var processName = p.ProcessName;
                    RetryHelper.RetryOnException($"Killing process consuming High CPU {processName}:{id}", () =>
                    {
                        p.Kill();
                        var eventMessage = $"CPU Monitoring - Process consuming High CPU killed  {processName}({id})";
                        AppendToMonitoringLog(eventMessage, true);
                        EventLog.WriteEntry("Application", eventMessage, EventLogEntryType.Information);
                    }, TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while killing process consuming High CPU", ex, sessionId);
            }
        }

        private void AppendToMonitoringLog(string message, bool logInKusto = false)
        {
            Interlocked.Increment(ref _loggerCount);
            string cpuMonitorPath = MonitoringSessionController.GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            string logFilePath = Path.Combine(cpuMonitorPath, Environment.MachineName + ".log");

            string logMessage = $"[{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow.ToString("hh:mm:ss")}] {message}{Environment.NewLine}";

            if (_loggerCount > MAX_LINES_IN_LOGFILE)
            {
                var sessionDirectory = GetLogsFolderForSession(_sessionId);
                var existingFileCount = FileSystemHelpers.GetFilesInDirectory(sessionDirectory, $"{Environment.MachineName}*.log", false, SearchOption.TopDirectoryOnly).Count;
                var newFileName = $"{Environment.MachineName}_{existingFileCount}.log";
                newFileName = Path.Combine(sessionDirectory, newFileName);
                FileSystemHelpers.MoveFile(logFilePath, newFileName);
                Interlocked.Exchange(ref _loggerCount, 0);
            }

            try
            {
                FileSystemHelpers.AppendAllTextToFile(logFilePath, logMessage);
            }
            catch (Exception)
            {
            }

            if (logInKusto)
            {
                Logger.LogCpuMonitoringEvent(message, _sessionId);
            }
        }

        private void ExecuteAction(string dumpFileInTempDirectory, int processId, string actionToExecute, string arguments)
        {
            arguments = arguments.Replace("{PROCESSID}", processId.ToString());
            arguments = arguments.Replace("{OUTPUTPATH}", dumpFileInTempDirectory);
            AppendToMonitoringLog($"Creating dump file with path {dumpFileInTempDirectory}", true);

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = actionToExecute,
                    Arguments = arguments,
                    UseShellExecute = false
                }
            };

            process.Start();
            process.WaitForExit();
        }

        private void CreateCustomActionFile(string sessionId, string customActionFile)
        {
            var sessionDirectory = GetLogsFolderForSession(sessionId);
            var sessionFileName = Path.Combine(sessionDirectory, customActionFile);
            FileSystemHelpers.AppendAllTextToFile(sessionFileName, $"Custom Action Executed on {Environment.MachineName} at {DateTime.UtcNow.ToString()} UTC");
        }

        private void MoveToPermanentStorage(string sessionId, string sourceFile, string fileName, string blobSasUri)
        {
            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                var sessionDirectory = GetLogsFolderForSession(sessionId);
                var collectFileName = Path.Combine(sessionDirectory, fileName);
                AppendToMonitoringLog($"Copying file from temp folders to [{collectFileName}]", true);
                try
                {
                    FileSystemHelpers.CopyFile(sourceFile, collectFileName);
                    AppendToMonitoringLog($"Copied file from temp folders to [{collectFileName}]", true);
                }
                catch (Exception ex)
                {
                    Logger.LogCpuMonitoringErrorEvent("Failed while moving file to Permanent FileSystem storage", ex, sessionId);
                }
            }
            else
            {
                try
                {
                    string relativeFilePath = Path.Combine("Monitoring", "Logs", sessionId, fileName);
                    Lease lease = Infrastructure.LeaseManager.TryGetLease(relativeFilePath, blobSasUri);
                    if (lease == null)
                    {
                        // This instance is already running this collector
                        Logger.LogCpuMonitoringVerboseEvent($"Could not get lease to upload the memory dump - {relativeFilePath}", sessionId);
                    }

                    AppendToMonitoringLog($"Copying {fileName} from temp folders to Blob Storage", true);
                    var accessCondition = AccessCondition.GenerateLeaseCondition(lease.Id);
                    var taskToUpload = Task.Run(() =>
                    {
                        FileStream fileStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var blockBlob = BlobController.GetBlobForFile(relativeFilePath, blobSasUri);
                        blockBlob.UploadFromStream(fileStream, accessCondition);

                    });

                    while(!taskToUpload.IsCompleted)
                    {
                        lease.Renew();
                        Logger.LogCpuMonitoringVerboseEvent($"Renewing lease to the blob file", sessionId);
                        Thread.Sleep(Infrastructure.Settings.LeaseRenewalTime);
                    }
                    lease.Release();
                    AppendToMonitoringLog($"Copied {fileName} from temp folders to Blob Storage", true);
                }
                catch (Exception ex)
                {
                    Logger.LogCpuMonitoringErrorEvent("Failed copying file to blob storage", ex, sessionId);
                }
                
            }
        }

        public static string GetLogsFolderForSession(string sessionId)
        {
            string logsFolderPath = MonitoringSessionController.GetCpuMonitoringPath(MonitoringSessionDirectories.Logs);
            string folderName = Path.Combine(logsFolderPath, sessionId);
            FileSystemHelpers.CreateDirectoryIfNotExists(folderName);
            return folderName;
        }

        private void RemoveOldProcessesFromMonitoringList(ConcurrentDictionary<int, MonitoredProcess> processList)
        {
            var oldProcesses = processList.Where(x => DateTime.UtcNow.Subtract(x.Value.LastMonitorTime).TotalMinutes > 1).Select(x => x.Key);
            foreach (var pid in oldProcesses)
            {
                if (processList.ContainsKey(pid))
                {
                    bool processRemoved = processList.TryRemove(pid, out MonitoredProcess removedValue);
                    if (processRemoved)
                    {
                        AppendToMonitoringLog($"Removing {pid} from monitoring list");
                    }
                }
            }
        }

    }
}
