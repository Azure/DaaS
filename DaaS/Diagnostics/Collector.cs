// -----------------------------------------------------------------------
// <copyright file="Collector.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.Leases;
using DaaS.Storage;

namespace DaaS.Diagnostics
{
    abstract class Collector : DiagnosticTool
    {
        public override string Name { get; internal set; }
        public string Command { get; set; }
        public string Arguments { get; set; }

        public string PredefinedValidator { get; set; }
        public string PreValidationCommand { get; set; }
        public string PreValidationArguments { get; set; }

        internal async Task<List<Log>> CollectLogs(DateTime utcStartTime, DateTime utcEndTime, string sessionId, string blobSasUri, string defaultHostName, CancellationToken ct)
        {
            if (DateTime.UtcNow < utcStartTime)
            {
                // Don't bother running collectors until the time range to collect has started
                Logger.LogDiagnostic($"It's not yet time to run this collector, utcStartTime = { utcStartTime.ToString() } and UtcNow = {DateTime.UtcNow.ToString()}");
                return null;
            }

            // Get a lease to run the collector on this instance
            var pathToLogs = GetRelativeStoragePath(utcStartTime, utcEndTime, defaultHostName);
            var logCollectionLease = Infrastructure.LeaseManager.TryGetLease(pathToLogs, blobSasUri);
            if (logCollectionLease == null)
            {
                // This instance is already running this collector
                Logger.LogDiagnostic("Could not get lease to log collection");
                return null;
            }

            // Check to see if logs for this time period already exists
            // (another collector session could have run with overlapping time fields or
            //   we have already run the collector but the results have not been saved to the session log yet)
            var logs = GetLogsForTimePeriod(utcStartTime, utcEndTime);
            if (logs == null || !logs.Any())
            {
                var outputDir = CreateTemporaryLogDestinationFolder(utcStartTime, utcEndTime, defaultHostName);

                var additionalError = string.Empty;
                //  Run the collector command & store logs to blob storage
                if (PreValidationSucceeded(out additionalError))
                {
                    bool collectorWasRun = await RunCollectorCommandAsync(utcStartTime, utcEndTime, outputDir, sessionId, logCollectionLease, ct);
                    if (!collectorWasRun)
                    {
                        return null;
                    }
                }
                else
                {
                    CreateCollectorWarningFile(outputDir, additionalError);
                }
                logs = MoveLogsToPermanentStorage(utcStartTime, utcEndTime, pathToLogs, blobSasUri, sessionId, defaultHostName);
            }

            if (logs == null || !logs.Any(x => !x.RelativePath.EndsWith(".diaglog")))
            {
                string collectorException = string.Empty;
                if (logs.Any(x => x.RelativePath.EndsWith(".err.diaglog")))
                {
                    var log = logs.FirstOrDefault(x => x.RelativePath.EndsWith(".err.diaglog"));
                    string errorFileName = Path.Combine(Infrastructure.Settings.TempDir, log.RelativePath.ConvertForwardSlashesToBackSlashes());
                    if (System.IO.File.Exists(errorFileName))
                    {
                        collectorException = System.IO.File.ReadAllText(errorFileName);
                    }
                }
                throw new DiagnosticToolHasNoOutputException(Name, collectorException);
            }

            // Mark this instance as having collected the logs
            logCollectionLease.Release();

            return logs.Where(x => !x.FileName.EndsWith(".diaglog")).ToList();
        }

        protected abstract Task<bool> RunCollectorCommandAsync(DateTime utcStartTime, DateTime utcEndTime, string outputDir, string sessionId, Lease lease, CancellationToken ct);

        private string GetRelativeStoragePath(DateTime utcStartTime, DateTime utcEndTime, string defaultHostName)
        {
            var path = Log.GetRelativeDirectory(utcStartTime, utcEndTime, this, defaultHostName);
            return path;
        }

        internal string CreateTemporaryLogDestinationFolder(DateTime utcStartTime, DateTime utcEndTime, string defaultHostName)
        {
            var relativeDirectoryPath = GetRelativeStoragePath(utcStartTime, utcEndTime, defaultHostName);
            var outputDir = Infrastructure.Storage.GetNewTempFolder(relativeDirectoryPath);
            return outputDir;
        }

        protected List<Log> MoveLogsToPermanentStorage(DateTime startTime, DateTime endTime, string outputDir, string blobSasUri, string sessionId, string defaultHostName)
        {
            // Once collector finishes executing, move log to permanent storage
            List<string> logFilesCollected = Infrastructure.Storage.GetFilesInDirectory(outputDir, StorageLocation.TempStorage, string.Empty);
            List<Log> logs = new List<Log>();
            List<Task> saveLogTasks = new List<Task>();
            foreach (var logFile in logFilesCollected.Where(x => !x.ToLower().EndsWith("diagstatus.diaglog")))
            {
                var fileSize = Infrastructure.Storage.GetFileSize(outputDir, logFile, StorageLocation.TempStorage);
                if (fileSize > 0)
                {
                    Logger.LogSessionVerboseEvent($"Adding log file {logFile} of size {fileSize} to list of logs", sessionId);
                }
                var filePath = logFile.Replace(Infrastructure.Settings.TempDir, "");
                var log = Log.GetLog(startTime, endTime, filePath, this, fileSize, blobSasUri, defaultHostName);
                logs.Add(log);
                Logger.LogSessionVerboseEvent($"Firing task to save the log file to permanent storage for {logFile}", sessionId);

                Task saveTask = null;
                if(!string.IsNullOrWhiteSpace(blobSasUri))
                {
                    saveTask = Infrastructure.Storage.UploadFileToBlobAsync(log.RelativePath, StorageLocation.TempStorage, blobSasUri, sessionId);
                }
                else
                {
                    saveTask = Infrastructure.Storage.SaveFileAsync(log, log.StorageLocation, blobSasUri);
                }
                saveLogTasks.Add(saveTask);
            }

            Logger.LogSessionVerboseEvent($"Waiting for all tasks to move files to permanent storage", sessionId);
            Task.WaitAll(saveLogTasks.ToArray());
            Logger.LogSessionVerboseEvent($"All Tasks to move files completed successfully", sessionId);

            Infrastructure.Storage.RemoveAllFilesInDirectory(outputDir, StorageLocation.TempStorage);

            return logs;
        }

        protected string ExpandVariablesInArgument(DateTime startTime, DateTime endTime, string outputDir)
        {
            var variables = new Dictionary<string, string>()
            {
                {"startTime", startTime.ToString("s")},
                {"endTime", endTime.ToString("s")},
                {"outputDir", outputDir}
            };
            var args = ExpandVariables(Arguments, variables);
            return args;
        }

        private List<Log> GetLogsForTimePeriod(DateTime utcStartTime, DateTime utcEndTime)
        {
            // TODO: Future feature - Add ability to pull logs previously stored for a given time period
            return null;
        }

        public bool PreValidationSucceeded(out string additionalErrorInfo)
        {
            bool ret = true;
            additionalErrorInfo = string.Empty;
            // if the PrevlidationForWarningCommand or PreValidationArguments is not specified for this colletor, we will
            // return true assuming warning is not needed
            try
            {
                PredefinedValidators validators = new PredefinedValidators();

                string predefinedValidator = Name + "Validator";
                MethodInfo validatorMethod = validators.GetType().GetMethod(predefinedValidator);
                if (validatorMethod != null)
                {
                    object[] parameters = new object[] { null };
                    bool validationSucceeded = (bool)validatorMethod.Invoke(validators, parameters);
                    if (!validationSucceeded)
                    {
                        additionalErrorInfo = (string)parameters[0];
                    }
                    return validationSucceeded;
                }

                if (string.IsNullOrWhiteSpace(PreValidationCommand))
                {                    
                    return true;
                }

                if (PreValidationArguments == null)
                {
                    PreValidationArguments = string.Empty;
                }

                var command = ExpandVariables(PreValidationCommand);
                var args = ExpandVariables(PreValidationArguments);

                Logger.LogDiagnostic("Calling {0}: {1} {2}", Name, command, args);

                // Call process using  RunProcess
                var toolProcess = Infrastructure.RunProcess(command, args,"");

                int num = 0;

                //we will wait for prevalidation to exit for at most 2 sec.
                while (!toolProcess.HasExited)
                {
                    num++;
                    Logger.LogDiagnostic("Checking if process exits: num = {0} ", num);
                    Thread.Sleep(100);

                    if (num >= 100)
                    {
                        break;
                    }
                }

                //if for some reason, the prevalidation command is not completed in 2 sec, we will assume that the prevalidation failed  so users
                //cannot enable the collector.  
                if (!toolProcess.HasExited)
                {
                    ret = false;
                    Logger.LogDiagnostic("The prevalidation command doesn't exit.");
                }
                else
                {
                    ret = (toolProcess.ExitCode == 0);
                    Logger.LogDiagnostic("Exit Code: {0} - The prevalidation command exited", toolProcess.ExitCode);
                }
            }
            catch (Exception e)
            {
                Logger.LogDiagnostic("Exception in RunPreValidationCommand: {0} \r\n {1}  ", e.Message, e.StackTrace);
            }

            return ret;
        }

        protected void CreateCollectorWarningFile(string outputDir, string additionalError)
        {
            try
            {
                //warning file is constructed using Collector name
                string WarningFilename = Path.Combine(outputDir, string.Format("{0}_NotEnabled.log", Name));
                string[] text = { Warning };
                if (!string.IsNullOrWhiteSpace(additionalError))
                {
                    text = new string[] { additionalError };
                }
                System.IO.File.WriteAllLines(WarningFilename, text);
            }
            catch (AggregateException e)
            {
                Logger.LogErrorEvent("Unexpected error occurred when running CreateCollectorWarningFile", e);
            }
        }
    }
}
