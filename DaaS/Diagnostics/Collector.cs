// -----------------------------------------------------------------------
// <copyright file="Collector.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Sessions;
using Microsoft.WindowsAzure.Storage.Blob;

namespace DaaS.Diagnostics
{

    internal class Collector : DiagnosticTool
    {
        public override string Name { get; internal set; }
        public string Command { get; set; }
        public string Arguments { get; set; }
        public string PreValidationCommand { get; set; } = string.Empty;
        public string PreValidationArguments { get; set; } = string.Empty;
        public string PreValidationMethod { get; set; } = string.Empty;

        public bool RequiresStorageAccount { get; set; }

        internal Collector(Diagnoser diagnoser)
        {
            Name = diagnoser.Name;
            Command = diagnoser.Collector.Command;
            Arguments = diagnoser.Collector.Arguments;
            PreValidationMethod = diagnoser.Collector.PreValidationMethod;
            PreValidationCommand = diagnoser.Collector.PreValidationCommand;
            PreValidationArguments = diagnoser.Collector.PreValidationArguments;
            RequiresStorageAccount = diagnoser.RequiresStorageAccount;
        }

        internal async Task<DiagnosticToolResponse> CollectLogsAsync(Session session, CancellationToken ct)
        {
            var resp = new DiagnosticToolResponse();

            string tempOutputDir = session.LogsTempDirectory;
            FileSystemHelpers.EnsureDirectory(tempOutputDir);

            //
            // Check to see if logs exists (another collector session could have run). This
            // will happen if DaasRunner restarts in the middle of a session collection
            //

            var logs = GetLogsForSession(tempOutputDir);
            if (!logs.Any())
            {
                var additionalError = string.Empty;
                var prevalSucceeded = PreValidationSucceeded(session.SessionId, out additionalError);
                if (prevalSucceeded)
                {
                    bool collectorWasRun = await RunCollectorCommandAsync(session, tempOutputDir, ct);
                    if (!collectorWasRun)
                    {
                        return resp;
                    }
                }
                else
                {
                    Logger.LogSessionWarningEvent($"Prevalidation failed for Collector: {session.Tool}", additionalError, session.SessionId);
                    resp.Errors.Add($"Prevalidation failed for '{session.Tool}'. {additionalError}");
                    return resp;
                }

                logs = GetLogsForSession(tempOutputDir);
                Logger.LogSessionVerboseEvent($"Collected '{string.Join(",", logs.Select(l => l.Name))}' log files for session", session.SessionId);
            }

            if (!logs.Any(x => !x.TempPath.EndsWith(".diaglog")))
            {
                string collectorException = string.Empty;
                if (logs.Any(x => x.TempPath.EndsWith(".err.diaglog")))
                {
                    var errorFile = logs.FirstOrDefault(x => x.TempPath.EndsWith(".err.diaglog"));
                    if (errorFile != null)
                    {
                        collectorException = File.ReadAllText(errorFile.TempPath);
                        resp.Errors.Add(collectorException);
                    }
                }

                throw new DiagnosticToolHasNoOutputException(Name, collectorException);
            }

            resp.Logs = logs.Where(x => !x.Name.EndsWith(".diaglog")).ToList();

            foreach (var log in resp.Logs)
            {
                log.Size = GetFileSize(log.TempPath);
                log.Name = Path.GetFileName(log.TempPath);
            }

            await CopyLogsToPermanentLocationAsync(resp, session, ct);

            Logger.LogSessionVerboseEvent($"Copied {resp.Logs.Count()} logs to permanent storage", session.SessionId);

            return resp;
        }

        private List<LogFile> GetLogsForSession(string tempOutputDir)
        {
            var logFiles = new List<LogFile>();
            var logsDirectory = new DirectoryInfo(tempOutputDir);

            foreach (var file in logsDirectory.GetFiles())
            {
                logFiles.Add(new LogFile()
                {
                    TempPath = file.FullName,
                    Name = Path.GetFileName(file.FullName),
                    Size = GetFileSize(file.FullName),
                    StartTime = file.LastWriteTimeUtc
                });
            }
            return logFiles;
        }

        private long GetFileSize(string path)
        {
            return new FileInfo(path).Length;
        }

        public bool PreValidationSucceeded(string sessionId, out string additionalErrorInfo)
        {
            bool ret = true;
            additionalErrorInfo = string.Empty;
            try
            {
                if (!string.IsNullOrWhiteSpace(PreValidationMethod))
                {
                    PredefinedValidators validators = new PredefinedValidators();
                    MethodInfo validatorMethod = validators.GetType().GetMethod(PreValidationMethod);
                    if (validatorMethod != null)
                    {
                        object[] parameters = new object[] { sessionId, null };
                        bool validationSucceeded = (bool)validatorMethod.Invoke(validators, parameters);
                        if (!validationSucceeded)
                        {
                            additionalErrorInfo = (string)parameters[1];
                        }

                        return validationSucceeded;
                    }
                }

                if (string.IsNullOrWhiteSpace(PreValidationCommand))
                {
                    return true;
                }

                var command = ExpandVariables(PreValidationCommand);
                var args = ExpandVariables(PreValidationArguments);

                Logger.LogDiagnostic("Calling {0}: {1} {2}", Name, command, args);

                // Call process using  RunProcess
                var toolProcess = DaaS.Infrastructure.RunProcess(command, args, "");

                int num = 0;

                //we will wait for prevalidation to exit for at most 2 sec.
                while (!toolProcess.HasExited)
                {
                    num++;
                    Logger.LogDiagnostic("Checking if process exits: num = {0} ", num);
                    Thread.Sleep(1000);

                    if (num >= 2)
                    {
                        break;
                    }
                }

                // if for some reason, the prevalidation command is not completed in 2 sec, we will assume that the prevalidation failed  so users
                // cannot enable the collector.  
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

        async Task<bool> RunCollectorCommandAsync(Session session, string outputDir, CancellationToken ct)
        {
            var args = ExpandVariablesInArgument(session.StartTime, outputDir);
            await RunProcessAsync(Command, args, session.SessionId, session.Description, ct);
            return true;
        }

        protected string ExpandVariablesInArgument(DateTime startTime, string outputDir)
        {
            var variables = new Dictionary<string, string>()
            {
                {"startTime", startTime.ToString("s")},
                {"outputDir", outputDir}
            };
            var args = ExpandVariables(Arguments, variables);
            return args;
        }

        private async Task CopyLogsToPermanentLocationAsync(DiagnosticToolResponse resp, Session activeSession, CancellationToken cancellationToken)
        {
            if (RequiresStorageAccount)
            {
                await CopyLogsToBlobStorageAsync(resp, activeSession, cancellationToken);
            }
            else
            {
                await CopyLogsToFileSystemAsync(resp, activeSession, cancellationToken);
            }
        }

        private async Task CopyLogsToBlobStorageAsync(DiagnosticToolResponse resp, Session activeSession, CancellationToken cancellationToken)
        {
            if (resp.Logs == null || !resp.Logs.Any())
            {
                return;
            }

            foreach (var log in resp.Logs)
            {
                string logPath = Path.Combine(
                    Settings.Instance.DefaultHostName,
                    activeSession.SessionId,
                    GetInstanceId(),
                    Path.GetFileName(log.TempPath));

                try
                {
                    var blob = Storage.BlobController.GetBlobForFile(logPath);
                    BlobRequestOptions blobRequestOptions = new BlobRequestOptions()
                    {
                        ServerTimeout = TimeSpan.FromMinutes(10)
                    };

                    Logger.LogSessionVerboseEvent($"Uploading {logPath} to blob storage", activeSession.SessionId);
                    await blob.UploadFromFileAsync(log.TempPath, null, blobRequestOptions, null, cancellationToken);
                    log.PartialPath = ConvertBackSlashesToForwardSlashes(logPath);
                    Logger.LogSessionVerboseEvent($"Uploaded {logPath} to blob storage", activeSession.SessionId);
                }
                catch (Exception ex)
                {
                    resp.Errors.Add($"Error '{ex.GetType()}:{ex.Message}' while copying {log.Name} to blob storage");
                    Logger.LogSessionErrorEvent($"Failed while copying {logPath} to Blob storage", ex, activeSession.SessionId);
                }
            }
        }

        private async Task CopyLogsToFileSystemAsync(DiagnosticToolResponse resp, Session activeSession, CancellationToken cancellationToken)
        {
            if (resp.Logs == null || !resp.Logs.Any())
            {
                return;
            }

            foreach (var log in resp.Logs)
            {
                string logPath = Path.Combine(
                    activeSession.SessionId,
                    GetInstanceId(),
                    Path.GetFileName(log.TempPath));

                log.PartialPath = ConvertBackSlashesToForwardSlashes(logPath, DaasDirectory.LogsDirRelativePath);
                string destination = Path.Combine(DaasDirectory.LogsDir, logPath);

                try
                {
                    await MoveFileAsync(log.TempPath, destination, activeSession.SessionId, cancellationToken);
                }
                catch (Exception ex)
                {
                    resp.Errors.Add($"Error '{ex.GetType()}:{ex.Message}' while copying {log.Name} to file system");
                    Logger.LogSessionErrorEvent($"Failed while copying {logPath} to file system", ex, activeSession.SessionId);
                }
            }
        }

        private string GetInstanceId()
        {
            return Environment.MachineName;
        }
    }
}
