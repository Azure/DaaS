// -----------------------------------------------------------------------
// <copyright file="IMonitoringRule.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;

namespace DaaS
{
    public class DiagnosticCpuRule : CpuMonitoringRuleBase, ICpuMonitoringRule
    {
        private readonly int _maxHours;

        public DiagnosticCpuRule(MonitoringSession session)
            : base(session)
        {
            _maxHours = session.MaximumNumberOfHours;
        }

        public void LogStartup(Action<string, bool> appendToMonitoringLog)
        {
            appendToMonitoringLog($"Monitoring started [CPU={_cpuThreshold}%, Mode={_sessionMode}, MaxActions={_maxActions}, Threshold={_thresholdSeconds}s, CheckEvery={_monitorDuration}s]", true);
        }

        public bool ShouldAnalyze(out string blobSasUri)
        {
            blobSasUri = _blobSasUri;
            return _sessionMode == SessionMode.CollectKillAndAnalyze;
        }

        public bool TakeActionOnHighCpu(int processId, string processName, DateTime monitoringStartTime, Action<string, bool> appendToMonitoringLog)
        {
            var actionsExecuted = GetTotalCustomActionsExecuted();
            string fileName = Environment.MachineName + "_" + processName + "_" + processId + "_" + DateTime.Now.Ticks.ToString();
            string outputPath = MonitoringSessionController.TempFilePath;
            FileSystemHelpers.CreateDirectoryIfNotExists(outputPath);
            string dumpFileInTempDirectory = Path.Combine(outputPath, fileName + ".dmp");

            if (_sessionMode != SessionMode.Kill)
            {
                appendToMonitoringLog($"Actions Executed on all instances = {actionsExecuted} of {_maxActions}", true);
            }

            bool dataCollected = false;
            if (ShouldCollectData())
            {
                CreateCustomActionFile(fileName, completed: false);
                ExecuteAction(dumpFileInTempDirectory, processId, appendToMonitoringLog);
                dataCollected = true;
            }

            KillProcessIfNeeded(processId, appendToMonitoringLog);

            if (dataCollected)
            {
                MoveToPermanentStorage(dumpFileInTempDirectory, fileName + ".dmp", appendToMonitoringLog);
                CreateCustomActionFile(fileName, completed: true);

                //
                // Since we copied the file to permanent storage, delete the temp file if the mode 
                // doesnt require analysis to be done or if the number of instances is more than 1
                //

                if (_sessionMode != SessionMode.CollectKillAndAnalyze || HeartBeats.HeartBeatController.GetNumberOfLiveInstances() > 1)
                {
                    FileSystemHelpers.DeleteFileSafe(dumpFileInTempDirectory);
                }
            }

            actionsExecuted = GetTotalCustomActionsExecuted();
            if (actionsExecuted >= _maxActions)
            {
                appendToMonitoringLog("Max number of actions configured for this session have executed so terminating shortly!", true);
                return true;
            }

            return false;
        }

        public bool ShouldTerminateRule(Action<string, bool> appendToMonitoringLog)
        {
            var actionsExecuted = GetTotalCustomActionsExecuted();

            bool terminateMonitoring = false;
            if (actionsExecuted >= _maxActions)
            {
                appendToMonitoringLog("Maximum number of actions configured on all instances have executed so terminating shortly!", true);
                terminateMonitoring = true;
            }

            if (DateTime.UtcNow.Subtract(_startDate).TotalHours >= _maxHours)
            {
                appendToMonitoringLog("Maximum time limit for this session has reached so terminating shortly!", true);
                terminateMonitoring = true;
            }

            if (terminateMonitoring)
            {
                var dumpsCollected = GetTotalCustomActionsCompleted();
                int sleepCount = 0;
                appendToMonitoringLog("Waiting for all instances to collect and move the dumps", true);
                while (dumpsCollected < actionsExecuted && sleepCount < 20)
                {
                    appendToMonitoringLog($"Total actions executed = {actionsExecuted} and dumps moved = {dumpsCollected}, waiting for the rest to finish", true);
                    Thread.Sleep(15000);
                    ++sleepCount;
                    actionsExecuted = GetTotalCustomActionsExecuted();
                    dumpsCollected = GetTotalCustomActionsCompleted();
                }
                appendToMonitoringLog("All instances finished collecting data so terminating", true);
                return true;
            }

            return false;
        }

        private bool ShouldCollectData()
        {
            return _sessionMode == SessionMode.CollectAndKill
                || _sessionMode == SessionMode.Collect
                || _sessionMode == SessionMode.CollectKillAndAnalyze;
        }

        private void KillProcessIfNeeded(int processId, Action<string, bool> appendToMonitoringLog)
        {
            if (_sessionMode == SessionMode.Collect)
            {
                return;
            }

            KillProcess(processId, appendToMonitoringLog);
        }
    }
}
