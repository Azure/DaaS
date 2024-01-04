// -----------------------------------------------------------------------
// <copyright file="CpuMonitoring.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DaaS
{
    public class CpuMonitoring
    {
        
        const int MaxLinesInLogFile = 40 * 1000;
        private int _loggerCount = 0;

        static ConcurrentDictionary<int, MonitoredProcess> ProcessList;
        static readonly List<string> _processesToMonitor = new List<string>();
        static string _sessionId = string.Empty;
        static readonly string[] _processesAlwaysExcluded = new string[] { "daasrunner", "daasconsole", "workerforwarder",
            "msvsmon", "cmd", "powershell", "SnapshotHolder_x64","SnapshotHolder_x86", "ApplicationInsightsProfiler",
            "VSDiagnostics", "clrprofilingcollector", "clrprofilinganalyzer","stacktracer32","stacktracer64", "jstackparser",
            "logparser","procdump", "procdump64", "cdb","loganalysisworker", "PhpReportGen","DumpAnalyzer", "dbghost",
            "MemoryDumpCollector", "SnapshotUploader64","SnapshotUploader86", "crashmon", "SnapshotHolder",
            "SnapshotUploader", "KuduHandles"};

        public double CpuUsageLastMinute
        {
            get;
            private set;
        }

        public void InitializeMonitoring(ICpuMonitoringRule rule)
        {
            if (rule != null)
            {
                _sessionId = rule.SessionId;

                rule.LogStartup(AppendToMonitoringLog);
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

        public bool MonitorCpu(ICpuMonitoringRule rule)
        {
            foreach (var process in Process.GetProcesses())
            {
                if (_processesToMonitor.Count > 0)
                {
                    if (!_processesToMonitor.Any(s => s.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                }

                if (!rule.MonitorScmProcesses)
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
                        CpuTimeStart = cpuTime,
                        Name = process.ProcessName,
                        CpuTimeCurrent = cpuTime,
                        LastMonitorTime = DateTime.UtcNow,
                        ThresholdExceededCount = 0,
                        ProcessStartTime = process.StartTime
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
                var start = ProcessList[id].CpuTimeStart;
                var oldCPUTime = ProcessList[id].CpuTimeCurrent;
                var processCpuTime = GetProcessCpuTime(id);

                if (processCpuTime != TimeSpan.MinValue)
                {
                    TimeSpan newCPUTime = processCpuTime - start;
                    var cpuTimeSeconds = (newCPUTime - oldCPUTime).TotalSeconds;
                    var durationSeconds = DateTime.UtcNow.Subtract(ProcessList[id].LastMonitorTime).TotalSeconds;

                    //
                    // For the first time CPU Time will be 
                    // negative as startTime is not subtracted
                    //

                    if (cpuTimeSeconds < 0)
                    {
                        CpuUsageLastMinute = 0;
                    }
                    else
                    {
                        CpuUsageLastMinute = cpuTimeSeconds / (Environment.ProcessorCount * durationSeconds);
                    }

                    ProcessList[id].CpuTimeCurrent = newCPUTime;
                    ProcessList[id].LastMonitorTime = DateTime.UtcNow; ;
                    var cpuPercent = CpuUsageLastMinute * 100;

                    processesMonitored.Add($"{ProcessList[id].Name}({id}):{cpuPercent:0} %");

                    bool terminateMonitoring = rule.ShouldTerminateRule(AppendToMonitoringLog);
                    if (terminateMonitoring)
                    {
                        return true;
                    }

                    if (cpuPercent >= rule.CpuThreshold)
                    {
                        AppendToMonitoringLog($"{ProcessList[id].Name}({id}) CPU:{cpuPercent:0.00} %");
                        int thresholdCount = ++ProcessList[id].ThresholdExceededCount;
                        int currentCpuConsumptionWithTime = thresholdCount * rule.MonitorDuration;
                        if (currentCpuConsumptionWithTime >= rule.ThresholdSeconds)
                        {
                            terminateMonitoring = rule.TakeActionOnHighCpu(id, ProcessList[id].Name, ProcessList[id].ProcessStartTime,  AppendToMonitoringLog);
                            ProcessList[id].ThresholdExceededCount = 0;
                            if (terminateMonitoring)
                            {
                                Thread.Sleep(5000);
                                return true;
                            }
                        }
                        else
                        {
                            AppendToMonitoringLog($"CPU Percent {cpuPercent:0.00} % > [{ rule.CpuThreshold } %] for {currentCpuConsumptionWithTime} seconds for { ProcessList[id].Name} ({ id }), waiting to reach threshold of {rule.ThresholdSeconds} seconds", true);
                        }
                    }
                    else
                    {
                        if (ProcessList[id].ThresholdExceededCount > 0)
                        {
                            AppendToMonitoringLog("Resetting CPU threshold", true);
                            ProcessList[id].ThresholdExceededCount = 0;
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

        private void AppendToMonitoringLog(string message, bool logInKusto = false)
        {
            Interlocked.Increment(ref _loggerCount);
            string cpuMonitorPath = MonitoringSessionController.GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            string logFilePath = Path.Combine(cpuMonitorPath, Environment.MachineName + ".log");

            string logMessage = $"[{DateTime.UtcNow.ToShortDateString()} {DateTime.UtcNow:hh:mm:ss}] {message}{Environment.NewLine}";

            if (_loggerCount > MaxLinesInLogFile)
            {
                var sessionDirectory = MonitoringSessionController.GetLogsFolderForSession(_sessionId);
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
