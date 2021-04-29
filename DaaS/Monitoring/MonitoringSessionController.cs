//-----------------------------------------------------------------------
// <copyright file="MonitoringSessionController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DaaS.Configuration;
using DaaS.Sessions;
using DaaS.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;

namespace DaaS
{
    public class MonitoringSessionController
    {
        const string MonitoringFolder = "Monitoring";

        const int MIN_CPU_THRESHOLD = 50;
        const int MAX_CUSTOM_ACTIONS = 20;
        const int MIN_MONITOR_DURATION_IN_SECONDS = 5;
        const int MIN_THRESHOLD_DURATION_IN_SECONDS = 15;
        readonly int MAX_SESSION_DURATION = (int)TimeSpan.FromDays(365).TotalHours;

        public readonly static string TempFilePath = Path.Combine(EnvironmentVariables.LocalTemp, "Monitoring", "Logs");

        public static string GetCpuMonitoringPath(string folderName = "", bool relativePath = false)
        {
            string path;
            if (relativePath)
            {
                path = Path.Combine(@"data\DaaS", MonitoringFolder, folderName);
                path.ConvertBackSlashesToForwardSlashes();
            }
            else
            {
                path = Path.Combine(Settings.UserSiteStorageDirectory, MonitoringFolder, folderName);
                FileSystemHelpers.CreateDirectoryIfNotExistsSafe(path);
            }

            return path;
        }
        public MonitoringSession CreateSession(MonitoringSession monitoringSession)
        {
            string cpuMonitoringActive = GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            var existingFiles = FileSystemHelpers.GetFilesInDirectory(cpuMonitoringActive, "*.json", false, SearchOption.TopDirectoryOnly);

            if (existingFiles.Count > 0)
            {
                throw new ApplicationException("Another monitoring session is already in progress");
            }
            else
            {
                ValidateSessionParameters(monitoringSession);
                FileSystemHelpers.DeleteDirectoryContentsSafe(cpuMonitoringActive);
                monitoringSession.StartDate = DateTime.UtcNow;
                monitoringSession.EndDate = DateTime.MinValue.ToUniversalTime();
                monitoringSession.SessionId = monitoringSession.StartDate.ToString(SessionConstants.SessionFileNameFormat);
                monitoringSession.BlobStorageHostName = BlobController.GetBlobStorageHostName(monitoringSession.BlobSasUri);
                cpuMonitoringActive = Path.Combine(cpuMonitoringActive, monitoringSession.SessionId + ".json");
                monitoringSession.SaveToDisk(cpuMonitoringActive);
                Logger.LogNewCpuMonitoringSession(monitoringSession);
            }

            return monitoringSession;
        }

        private void ValidateSessionParameters(MonitoringSession monitoringSession)
        {
            if (monitoringSession.CpuThreshold < MIN_CPU_THRESHOLD)
            {
                throw new InvalidOperationException($"CpuThreshold cannot be less than {MIN_CPU_THRESHOLD} percent");
            }

            if (monitoringSession.MaxActions > MAX_CUSTOM_ACTIONS)
            {
                throw new InvalidOperationException($"MaxActions cannot be more than {MAX_CUSTOM_ACTIONS} actions");
            }

            if (monitoringSession.MaximumNumberOfHours > MAX_SESSION_DURATION)
            {
                throw new InvalidOperationException($"MaximumNumberOfHours cannot be more than {MAX_SESSION_DURATION} hours");
            }

            if (monitoringSession.MonitorDuration < MIN_MONITOR_DURATION_IN_SECONDS)
            {
                throw new InvalidOperationException($"MonitorDuration cannot be less than {MIN_MONITOR_DURATION_IN_SECONDS} seconds");
            }
            if (monitoringSession.ThresholdSeconds < MIN_THRESHOLD_DURATION_IN_SECONDS)
            {
                throw new InvalidOperationException($"ThresholdSeconds cannot be less than {MIN_THRESHOLD_DURATION_IN_SECONDS} seconds");
            }
        }

        public MonitoringSession GetSession(string sessionId)
        {
            string cpuMonitoringCompleted = GetCpuMonitoringPath(MonitoringSessionDirectories.Completed);
            var sessionFilePath = Path.Combine(cpuMonitoringCompleted, sessionId + ".json");
            if (FileSystemHelpers.FileExists(sessionFilePath))
            {
                var monitoringSession = FileSystemHelpers.FromJsonFile<MonitoringSession>(sessionFilePath);
                return monitoringSession;
            }
            else
            {
                return null;
            }
        }

        public string AnalyzeSession(string sessionId, string blobSasUri)
        {
            var session = GetSession(sessionId);

            if (session == null)
            {
                throw new InvalidOperationException("Session does not exist or is not yet completed");
            }

            foreach (var log in session.FilesCollected)
            {
                if (string.IsNullOrWhiteSpace(log.ReportFile) && !string.IsNullOrWhiteSpace(log.FileName))
                {
                    MonitoringAnalysisController.QueueAnalysisRequest(sessionId, log.FileName, blobSasUri);
                }
            }
            session.AnalysisStatus = AnalysisStatus.InProgress;
            SaveSession(session);
            return "Analysis request submitted";
        }

        public void DeleteSession(string sessionId)
        {
            string cpuMonitoringCompleted = GetCpuMonitoringPath(MonitoringSessionDirectories.Completed);
            var sessionFilePath = Path.Combine(cpuMonitoringCompleted, sessionId + ".json");
            if (FileSystemHelpers.FileExists(sessionFilePath))
            {
                FileSystemHelpers.DeleteFileSafe(sessionFilePath);
            }

            string logsFolderPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Logs);
            string logsFolder = Path.Combine(logsFolderPath, sessionId);

            if (FileSystemHelpers.DirectoryExists(logsFolder))
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(logsFolder);
                FileSystemHelpers.DeleteDirectorySafe(logsFolder);
            }
            Logger.LogCpuMonitoringVerboseEvent("Deleted session", sessionId);
        }

        public void TerminateActiveMonitoringSession()
        {
            string cpuMonitorPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            var existingFiles = FileSystemHelpers.GetFilesInDirectory(cpuMonitorPath, "*.json", false, SearchOption.TopDirectoryOnly);
            if (existingFiles.Count > 0)
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(cpuMonitorPath, true);
            }
        }

        public bool StopMonitoringSession()
        {
            Logger.LogCpuMonitoringVerboseEvent($"Inside the StopMonitoringSession method of MonitoringSessionController", string.Empty);
            string cpuMonitoringActivePath = GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            var existingFiles = FileSystemHelpers.GetFilesInDirectory(cpuMonitoringActivePath, "*.json", false, SearchOption.TopDirectoryOnly);

            if (existingFiles.Count > 0)
            {
                var monitoringSession = FileSystemHelpers.FromJsonFile<MonitoringSession>(existingFiles.FirstOrDefault());
                Logger.LogCpuMonitoringVerboseEvent($"Stopping an active session {existingFiles.FirstOrDefault()}", monitoringSession.SessionId);
                var canwriteToFileSystem = CheckAndWaitTillFileSystemWritable(monitoringSession.SessionId);
                if (!canwriteToFileSystem)
                {
                    return false;
                }

                try
                {
                    monitoringSession.EndDate = DateTime.UtcNow;
                    string cpuMonitorCompletedPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Completed);
                    cpuMonitorCompletedPath = Path.Combine(cpuMonitorCompletedPath, monitoringSession.SessionId + ".json");

                    if (!FileSystemHelpers.FileExists(cpuMonitorCompletedPath))
                    {
                        monitoringSession.FilesCollected = GetCollectedLogsForSession(monitoringSession.SessionId, monitoringSession.BlobSasUri);
                        Logger.LogCpuMonitoringVerboseEvent($"Found {monitoringSession.FilesCollected.Count} files collected by CPU monitoring", monitoringSession.SessionId);
                        SaveSession(monitoringSession);
                        MoveMonitoringLogsToSession(monitoringSession.SessionId);
                    }
                    else
                    {
                        // some other instance probably ended up writing the file
                        // lets hope that finishes and files get moved properly
                    }

                    //
                    // Now delete the Active Session File
                    //
                    try
                    {
                        FileSystemHelpers.DeleteFileSafe(existingFiles.FirstOrDefault(), false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogCpuMonitoringErrorEvent("Failed while deleting the Active session file", ex, monitoringSession.SessionId);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogCpuMonitoringErrorEvent("Failed while marking a session as Complete", ex, monitoringSession.SessionId);
                    return false;
                }
            }

            return true;
        }

        public List<MonitoringFile> GetCollectedLogsForSession(string sessionId, string blobSasUri)
        {
            var filesCollected = new List<MonitoringFile>();

            try
            {
                if (string.IsNullOrWhiteSpace(blobSasUri))
                {
                    string folderName = CpuMonitoring.GetLogsFolderForSession(sessionId);
                    if (FileSystemHelpers.DirectoryExists(folderName))
                    {
                        var logFiles = FileSystemHelpers.GetFilesInDirectory(folderName, "*.dmp", false, SearchOption.TopDirectoryOnly);
                        foreach (var fileName in logFiles)
                        {
                            string relativePath = MonitoringFile.GetRelativePath(sessionId, Path.GetFileName(fileName));
                            filesCollected.Add(new MonitoringFile(fileName, relativePath));
                        }
                    }
                }
                else
                {
                    string directoryPath = Path.Combine("Monitoring", "Logs", sessionId);
                    List<string> files = new List<string>();
                    var dir = BlobController.GetBlobDirectory(directoryPath, blobSasUri);
                    foreach (
                        IListBlobItem item in
                            dir.ListBlobs(useFlatBlobListing: true))
                    {
                        var relativePath = item.Uri.ToString().Replace(item.Container.Uri.ToString() + "/", "");
                        string fileName = item.Uri.Segments.Last();
                        filesCollected.Add(new MonitoringFile(fileName, relativePath));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while getting the list of logs collected for the session", ex, sessionId);
            }
            
            return filesCollected;
        }

        internal void AddReportToLog(string sessionId, string logfileName, string reportFilePath, List<string> errors, bool shouldUpdateSessionStatus = true)
        {
            var session = GetSession(sessionId);
            var lockFile = AcquireSessionLock(session);

            foreach (var log in session.FilesCollected)
            {
                if (log.FileName.Equals(logfileName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(reportFilePath))
                    {
                        log.ReportFile = reportFilePath;
                        log.ReportFileRelativePath = MonitoringFile.GetRelativePath(sessionId, Path.GetFileName(reportFilePath));
                    }
                    if (errors != null && errors.Count > 0)
                    {
                        if (log.AnalysisErrors != null)
                        {
                            log.AnalysisErrors.Concat(errors);
                        }
                        else
                        {
                            log.AnalysisErrors = errors;
                        }
                    }
                    break;
                }
            }

            if (shouldUpdateSessionStatus)
            {
                bool sessionAnalysisPending = session.FilesCollected.Any(log => string.IsNullOrWhiteSpace(log.ReportFile) && (log.AnalysisErrors == null));
                if (!sessionAnalysisPending)
                {
                    session.AnalysisStatus = AnalysisStatus.Completed;
                }
            }
            SaveSession(session, lockFile);
        }

        private bool CheckAndWaitTillFileSystemWritable(string sessionId)
        {
            int maxWaitCount = 6, waitCount = 0;
            bool isFileSystemReadOnly = FileSystemHelpers.IsFileSystemReadOnly();
            if (isFileSystemReadOnly)
            {
                Logger.LogCpuMonitoringVerboseEvent("Waiting till filesystem is readonly", sessionId);
                while (isFileSystemReadOnly && (waitCount <= maxWaitCount))
                {
                    isFileSystemReadOnly = FileSystemHelpers.IsFileSystemReadOnly();
                    Thread.Sleep(10 * 1000);
                    ++waitCount;
                }

                if (waitCount >= maxWaitCount)
                {
                    Logger.LogCpuMonitoringVerboseEvent("FileSystem is still readonly so exiting...", sessionId);
                    return false;
                }
                Logger.LogCpuMonitoringVerboseEvent("FileSystem is no more readonly", sessionId);
            }
            return true;
        }

        private LockFile AcquireSessionLock(MonitoringSession session, string methodName = "")
        {
            string sessionFilePath = (session.EndDate != DateTime.MinValue.ToUniversalTime()) ? GetCpuMonitoringPath(MonitoringSessionDirectories.Completed) : GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            string lockFilePath = sessionFilePath + ".lock";

            LockFile _lockFile = new LockFile(lockFilePath);
            int loopCount = 0;
            int lognum = 1;
            int maximumWaitTimeInSeconds = 15 * 60;

            while (!_lockFile.Lock($"AcquireSessionLock by {methodName} on {Environment.MachineName}") && loopCount <= maximumWaitTimeInSeconds)
            {
                ++loopCount;
                if (loopCount > lognum * 120)
                {
                    ++lognum;
                    Logger.LogCpuMonitoringVerboseEvent($"Waiting to acquire the lock on session file , loop {lognum}", session.SessionId);
                }
                Thread.Sleep(1000);
            }
            if (loopCount == maximumWaitTimeInSeconds)
            {
                Logger.LogCpuMonitoringVerboseEvent($"Deleting the lock file as it seems to be in an orphaned stage", session.SessionId);
                _lockFile.Release();
                return null;
            }
            return _lockFile;
        }

        public void SaveSession(MonitoringSession session, LockFile lockFile = null)
        {
            if (lockFile == null)
            {
                lockFile = AcquireSessionLock(session);
            }

            string cpuMonitorCompletedPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Completed);
            cpuMonitorCompletedPath = Path.Combine(cpuMonitorCompletedPath, session.SessionId + ".json");
            session.SaveToDisk(cpuMonitorCompletedPath);
            if (lockFile != null)
            {
                lockFile.Release();
            }
        }

        private void MoveMonitoringLogsToSession(string sessionId)
        {
            try
            {
                string logsFolderPath = CpuMonitoring.GetLogsFolderForSession(sessionId);
                string monitoringFolderActive = GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
                var filesCollected = FileSystemHelpers.GetFilesInDirectory(monitoringFolderActive, "*.log", false, SearchOption.TopDirectoryOnly);
                foreach (string monitoringLog in filesCollected)
                {
                    string fileName = Path.GetFileName(monitoringLog);
                    fileName = Path.Combine(logsFolderPath, fileName);
                    Logger.LogCpuMonitoringVerboseEvent($"Moving {monitoringLog} to {fileName}", sessionId);
                    RetryHelper.RetryOnException("Moving monitoring log to logs folder...", () =>
                    {
                        FileSystemHelpers.MoveFile(monitoringLog, fileName);
                    }, TimeSpan.FromSeconds(5), 5);
                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed while moving monitoring logs for the session", ex, sessionId);
            }
        }

        public List<MonitoringSession> GetAllCompletedSessions()
        {
            var sessions = new List<MonitoringSession>();
            string cpuMonitorPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Completed);

            try
            {
                var existingSessions = FileSystemHelpers.GetFilesInDirectory(cpuMonitorPath, "*.json", false, SearchOption.TopDirectoryOnly);
                foreach (var session in existingSessions)
                {
                    var monitoringSession = FileSystemHelpers.FromJsonFile<MonitoringSession>(session);
                    sessions.Add(monitoringSession);
                }
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Failed to get completed monitoring sessions", ex, string.Empty);
            }
            
            return sessions;
        }

        public MonitoringSession GetActiveSession()
        {
            string cpuMonitorPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            var existingFiles = FileSystemHelpers.GetFilesInDirectory(cpuMonitorPath, "*.json", false, SearchOption.TopDirectoryOnly);

            if (existingFiles.Count > 0)
            {
                Logger.LogDiagnostic($"Found an active monitoring session {existingFiles.FirstOrDefault()}");
                var session = FileSystemHelpers.FromJsonFile<MonitoringSession>(existingFiles.FirstOrDefault());
                return session;
            }
            else
            {
                Logger.LogDiagnostic($"Found no active monitoring session");
                return null;
            }
        }

        public IEnumerable<MonitoringLogsPerInstance> GetActiveSessionMonitoringLogs()
        {
            var logs = new List<MonitoringLogsPerInstance>();
            string cpuMonitorPath = GetCpuMonitoringPath(MonitoringSessionDirectories.Active);
            var activeInstances = HeartBeats.HeartBeatController.GetLiveInstances();

            if (GetActiveSession() != null)
            {
                foreach (var logFile in FileSystemHelpers.GetFilesInDirectory(cpuMonitorPath, "*.log", false, SearchOption.TopDirectoryOnly))
                {
                    string instanceName = Path.GetFileNameWithoutExtension(logFile);
                    if (activeInstances.Any(x => x.Name.Equals(instanceName, StringComparison.OrdinalIgnoreCase)))
                    {
                        string logContent = ReadEndTokens(logFile, 10, Encoding.Default, Environment.NewLine);
                        logs.Add(new MonitoringLogsPerInstance()
                        {
                            Instance = instanceName,
                            Logs = logContent
                        });
                    }
                }
            }

            return logs;
        }

        //
        // Get last 10 lines of very large text file > 10GB
        // https://stackoverflow.com/questions/398378/get-last-10-lines-of-very-large-text-file-10gb
        //
        private string ReadEndTokens(string path, long numberOfTokens, Encoding encoding, string tokenSeparator)
        {

            int sizeOfChar = encoding.GetByteCount("\n");
            byte[] buffer = encoding.GetBytes(tokenSeparator);


            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                long tokenCount = 0;
                long endPosition = fs.Length / sizeOfChar;

                for (long position = sizeOfChar; position < endPosition; position += sizeOfChar)
                {
                    fs.Seek(-position, SeekOrigin.End);
                    fs.Read(buffer, 0, buffer.Length);

                    if (encoding.GetString(buffer) == tokenSeparator)
                    {
                        tokenCount++;
                        if (tokenCount == numberOfTokens)
                        {
                            byte[] returnBuffer = new byte[fs.Length - fs.Position];
                            fs.Read(returnBuffer, 0, returnBuffer.Length);
                            return encoding.GetString(returnBuffer);
                        }
                    }
                }

                // handle case where number of tokens in file is less than numberOfTokens
                fs.Seek(0, SeekOrigin.Begin);
                buffer = new byte[fs.Length];
                fs.Read(buffer, 0, buffer.Length);
                return encoding.GetString(buffer);
            }
        }
    }
}
