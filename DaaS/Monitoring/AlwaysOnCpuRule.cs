// -----------------------------------------------------------------------
// <copyright file="IMonitoringRule.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using DaaS.Storage;

namespace DaaS
{
    public class AlwaysOnCpuRule : CpuMonitoringRuleBase, ICpuMonitoringRule
    {
        const int MaxDumpsToKeepOnStorage = 10;

        private readonly int _intervalDays;
        private readonly int _actionsInInterval;

        public AlwaysOnCpuRule(MonitoringSession session)
            : base(session)
        {
            _actionsInInterval = session.ActionsInInterval;
            _intervalDays = session.IntervalDays;
        }

        public void LogStartup(Action<string, bool> appendToMonitoringLog)
        {
            appendToMonitoringLog($"Monitoring started [CPU={_cpuThreshold}%, " 
                + $"RuleType=AlwaysOn, Threshold={_thresholdSeconds}s, ActionsInInterval={_actionsInInterval}, "
                + $"IntervalDays={_intervalDays} CheckEvery={_monitorDuration}s]", true);
        }

        public bool ShouldAnalyze(out string blobSasUri)
        {
            blobSasUri = _blobSasUri;
            return false;
        }

        public bool TakeActionOnHighCpu(int processId, string processName, Action<string, bool> appendToMonitoringLog)
        {
            string fileName = Environment.MachineName + "_" + processName + "_" + processId + "_" + DateTime.Now.Ticks.ToString();
            string outputPath = MonitoringSessionController.TempFilePath;
            FileSystemHelpers.CreateDirectoryIfNotExists(outputPath);
            string dumpFileInTempDirectory = Path.Combine(outputPath, fileName + ".dmp");

            bool dataCollected = false;
            if (ShouldCollectData())
            {
                CreateCustomActionFile(fileName, completed: false);
                ExecuteAction(dumpFileInTempDirectory, processId, appendToMonitoringLog);
                dataCollected = true;
            }

            KillProcessIfNeeded(processId, appendToMonitoringLog);
            DeleteOlderFilesFromBlob();

            if (dataCollected)
            {
                MoveToPermanentStorage(dumpFileInTempDirectory, fileName + ".dmp", appendToMonitoringLog);

                //
                // Since we copied the file to permanent storage, delete the temp file if
                // the number of instances is more than 1
                //

                if (HeartBeats.HeartBeatController.GetNumberOfLiveInstances() > 1)
                {
                    FileSystemHelpers.DeleteFileSafe(dumpFileInTempDirectory);
                }
            }

            return false;
        }

        private void DeleteOlderFilesFromBlob()
        {
            try
            {
                string directoryPath = Path.Combine("Monitoring", "Logs", _sessionId);
                var blobs = BlobController.GetBlobs(directoryPath, _blobSasUri);
                if (blobs.Count() < MaxDumpsToKeepOnStorage)
                {
                    return;
                }

                foreach (var blob in blobs.OrderByDescending(x => x.Properties.LastModified).Skip(MaxDumpsToKeepOnStorage))
                {
                    string blobName = blob.Name;
                    blob.Delete();
                    Logger.LogVerboseEvent($"Deleted blob {blobName}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while deleting old dumps", ex, _sessionId);
            }
        }

        private bool ShouldCollectData()
        {
            try
            {
                int currentActionsInInterval = 0;
                var sessionDirectory = GetLogsFolderForSession();
                var actionFilesExecuted = FileSystemHelpers.GetFilesInDirectory(sessionDirectory, "*" + CustomActionFileExt, false, SearchOption.TopDirectoryOnly);
                foreach (var actionFile in actionFilesExecuted)
                {
                    var customActionFile = FileSystemHelpers.FromJsonFile<CustomActionFileContents>(actionFile);
                    if (DateTime.UtcNow.Subtract(customActionFile.Created).TotalDays > _intervalDays)
                    {
                        //
                        // Delete custom action files that were generated before the current inerval
                        //
                        
                        FileSystemHelpers.DeleteFileSafe(actionFile);
                    }
                    else
                    {
                        ++currentActionsInInterval;
                    }
                }

                return currentActionsInInterval < _actionsInInterval;
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed in AlwaysOnCpuRule - ShouldCollectData", ex, _sessionId);
            }

            return false;
        }

        public bool ShouldTerminateRule(Action<string, bool> appendToMonitoringLog)
        {
            return false;
        }

        private void KillProcessIfNeeded(int processId, Action<string, bool> appendToMonitoringLog)
        {
            KillProcess(processId, appendToMonitoringLog);
        }
    }
}
