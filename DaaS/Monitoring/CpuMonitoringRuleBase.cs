// -----------------------------------------------------------------------
// <copyright file="CpuMonitoringRuleBase.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DaaS.Configuration;
using DaaS.Storage;
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
        private readonly IStorageService _storageService;
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
            _storageService = new AzureStorageService();
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
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            process.OutputDataReceived += (o, a) =>
            {
                outputBuilder.AppendLine(a.Data ?? string.Empty);
            };
            process.ErrorDataReceived += (o, a) =>
            {
                errorBuilder.AppendLine(a.Data ?? string.Empty);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            appendToMonitoringLog($"Action executed with exit code: {process.ExitCode}", true);

            if (!string.IsNullOrWhiteSpace(outputBuilder.ToString()))
            {
                appendToMonitoringLog($"Action executed with output {CleanProcdumpOutput(outputBuilder)}", true);
            }
            
            if (!string.IsNullOrWhiteSpace(errorBuilder.ToString()))
            {
                appendToMonitoringLog($"Action executed with error {errorBuilder}", true);
            }
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
                appendToMonitoringLog($"Copying {fileName} from temp folders to Blob Storage", true);

                try
                {
                    var uri = _storageService.UploadFileAsync(sourceFile, relativeFilePath, CancellationToken.None).Result;
                    if (EnqueueEventToAzureQueue(fileName, uri.ToString()))
                    {
                        appendToMonitoringLog("Message dropped successfully in Azure Queue for alerting", false);
                    }
                }
                catch (Exception ex)
                {
                    appendToMonitoringLog("Failed uploading file to blob storage - " + ex.Message, true);
                    Logger.LogCpuMonitoringErrorEvent("Failed uploading file to blob storage", ex, _sessionId);
                    throw;
                }
                appendToMonitoringLog($"Copied {fileName} from temp folders to Blob Storage", true);
            }
            catch (Exception ex)
            {
                appendToMonitoringLog("Failed copying file to blob storage - " + ex.Message, true);
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

        private string CleanProcdumpOutput(StringBuilder outputBuilder)
        {
            string procDumpOutput = outputBuilder.ToString();
            try
            {
                var procDumpOutputArray = procDumpOutput.Split(Environment.NewLine.ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                procDumpOutputArray = procDumpOutputArray.Where(s => !s.Contains("Mark Russinovich") && !s.Contains("process dump utility") && !s.Contains("www.sysinternals.com")).ToArray();
                return string.Join(Environment.NewLine, procDumpOutputArray);
            }
            catch(Exception ex)
            {
                Logger.LogCpuMonitoringEvent($"Failed while cleaning procdump output: {ex}", _sessionId);
            }

            return procDumpOutput;
        }
    }
}
