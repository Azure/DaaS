// -----------------------------------------------------------------------
// <copyright file="CpuMonitoringRuleBase.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Leases;
using DaaS.Storage;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;

namespace DaaS
{
    public class CustomActionFileContents
    {
        public DateTime Created { get; set; }
        public string Message { get; set; }
    }
    public class CpuMonitoringRuleBase
    {
        protected const string CustomActionFileExt = ".customaction";
        protected const string CustomActionCompletedFileExt = ".customactioncompleted";

        protected readonly int _monitorDuration;
        protected readonly int _cpuThreshold;
        protected readonly int _thresholdSeconds;
        protected readonly int _maxActions;
        protected readonly string _sessionId;
        protected readonly string _actionToExecute;
        protected readonly string _arguments;
        protected readonly bool _monitorScmProcess;
        protected readonly SessionMode _sessionMode;
        protected readonly DateTime _startDate;
        protected readonly string _defaultHostName;

        public bool MonitorScmProcesses => _monitorScmProcess;
        public int MonitorDuration => _monitorDuration;
        public int CpuThreshold => _cpuThreshold;
        public int ThresholdSeconds => _thresholdSeconds;
        public string SessionId => _sessionId;

        protected readonly AlertingStorageQueue _alertingStorageQueue = new AlertingStorageQueue();

        public CpuMonitoringRuleBase(MonitoringSession session)
        {
            _sessionMode = session.Mode;
            _startDate = session.StartDate;
            _monitorScmProcess = session.MonitorScmProcesses;
            _monitorDuration = session.MonitorDuration;
            _thresholdSeconds = session.ThresholdSeconds;
            _cpuThreshold = session.CpuThreshold;
            _maxActions = session.MaxActions;
            _sessionId = session.SessionId;
            _defaultHostName = session.DefaultHostName;
            _actionToExecute = string.IsNullOrWhiteSpace(session.ActionToExecute) ? EnvironmentVariables.ProcdumpPath : session.ActionToExecute;
            _arguments = string.IsNullOrWhiteSpace(session.ArgumentsToAction) ? " -accepteula -dc \"{MEMORYDUMPCOMMENT}\" -ma {PROCESSID} {OUTPUTPATH}" : session.ArgumentsToAction;
        }

        protected void ExecuteAction(string dumpFileName, int processId, Action<string, bool> appendToMonitoringLog)
        {
            var arguments = _arguments.Replace("{PROCESSID}", processId.ToString());
            arguments = arguments.Replace("{OUTPUTPATH}", dumpFileName);
            arguments = arguments.Replace("{MEMORYDUMPCOMMENT}", $"CPU above {_cpuThreshold}% for {_thresholdSeconds}s");
            appendToMonitoringLog($"Creating dump file with path {dumpFileName}", true);

            var process = new Process()
            {
                StartInfo =
                {
                    FileName = _actionToExecute,
                    Arguments = arguments,
                    UseShellExecute = false
                }
            };

            process.Start();
            process.WaitForExit();
        }

        protected void CreateCustomActionFile(string fileName, bool completed)
        {
            string customActionFile = fileName + CustomActionFileExt;
            if (completed)
            {
                customActionFile = fileName + CustomActionCompletedFileExt;
            }
            var sessionDirectory = GetLogsFolderForSession();
            var sessionFileName = Path.Combine(sessionDirectory, customActionFile);
            FileSystemHelpers.AppendAllTextToFile(sessionFileName,
                JsonConvert.SerializeObject(new CustomActionFileContents()
                {
                    Created = DateTime.UtcNow,
                    Message = $"Custom Action Executed on {Environment.MachineName}"
                }
            ));
        }

        protected int GetTotalCustomActionsExecuted()
        {
            var sessionDirectory = GetLogsFolderForSession();
            return FileSystemHelpers.GetFilesInDirectory(sessionDirectory, "*" + CustomActionFileExt, false, SearchOption.TopDirectoryOnly).Count;
        }

        protected int GetTotalCustomActionsCompleted()
        {
            var sessionDirectory = GetLogsFolderForSession();
            return FileSystemHelpers.GetFilesInDirectory(sessionDirectory, "*" + CustomActionCompletedFileExt, false, SearchOption.TopDirectoryOnly).Count;

        }

        protected string GetLogsFolderForSession()
        {
            return MonitoringSessionController.GetLogsFolderForSession(_sessionId);
        }

        protected bool EnqueueEventToAzureQueue(string fileName, string fileNameOnBlob)
        {
            if (_alertingStorageQueue == null)
            {
                return false;
            }

            var message = new
            {
                Category = "CpuMonitoring",
                TimeStampUtc = DateTime.UtcNow,
                SiteName = Settings.Instance.DefaultHostName,
                SessionId = _sessionId,
                FileName = fileName,
                BlobFileName = fileNameOnBlob
            };

            try
            {
                return _alertingStorageQueue.WriteMessageToAzureQueue(JsonConvert.SerializeObject(message));
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Unhandled exception while writing to AlertingStorageQueue", ex, _sessionId);
            }

            return false;
        }

        protected void MoveToPermanentStorage(string sourceFile, string fileName, Action<string, bool> appendToMonitoringLog)
        {
            try
            {
                string relativeFilePath = Path.Combine(MonitoringSessionController.GetRelativePathForSession(_defaultHostName, _sessionId), fileName);
                Lease lease = Infrastructure.LeaseManager.TryGetLease(relativeFilePath);
                if (lease == null)
                {
                    // This instance is already running this collector
                    Logger.LogCpuMonitoringVerboseEvent($"Could not get lease to upload the memory dump - {relativeFilePath}", _sessionId);
                }

                appendToMonitoringLog($"Copying {fileName} from temp folders to Blob Storage", true);
                var accessCondition = AccessCondition.GenerateLeaseCondition(lease.Id);
                var taskToUpload = Task.Run(() =>
                {
                    var blockBlob = BlobController.GetBlobForFile(relativeFilePath);
                    blockBlob.UploadFromFile(sourceFile, accessCondition);
                    if (EnqueueEventToAzureQueue(fileName, blockBlob.Uri.ToString()))
                    {
                        appendToMonitoringLog("Message dropped successfully in Azure Queue for alerting", false);
                    }
                });

                while (!taskToUpload.IsCompleted)
                {
                    lease.Renew();
                    Logger.LogCpuMonitoringVerboseEvent($"Renewing lease to the blob file", _sessionId);
                    Thread.Sleep(Infrastructure.Settings.LeaseRenewalTime);
                }
                lease.Release();
                appendToMonitoringLog($"Copied {fileName} from temp folders to Blob Storage", true);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed copying file to blob storage", ex, _sessionId);
            }
        }

        protected void KillProcess(int processId, Action<string, bool> appendToMonitoringLog)
        {
            try
            {
                var p = Process.GetProcessById(processId);
                if (p != null)
                {
                    var processName = p.ProcessName;
                    RetryHelper.RetryOnException($"Killing process consuming High CPU {processName}:{processId}", () =>
                    {
                        p.Kill();
                        var eventMessage = $"CPU Monitoring - Process consuming High CPU killed  {processName}({processId})";
                        appendToMonitoringLog(eventMessage, true);
                        EventLog.WriteEntry("Application", eventMessage, EventLogEntryType.Information);
                    }, TimeSpan.FromSeconds(1));
                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while killing process consuming High CPU", ex, _sessionId);
            }
        }
    }
}
