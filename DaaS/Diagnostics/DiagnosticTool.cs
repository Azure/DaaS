// -----------------------------------------------------------------------
// <copyright file="DiagnosticTool.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS.Diagnostics;
using DaaS.Leases;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS.Diagnostics
{
    abstract class DiagnosticTool
    {
        public virtual string Name { get; internal set; }

        protected static string ExpandVariables(string input, Dictionary<string, string> variables = null)
        {
            if (variables == null)
            {
                variables = new Dictionary<string, string>();
            }

            var diagnosticToolsPath = Infrastructure.Settings.GetDiagnosticToolsPath();

            Logger.LogDiagnostic("Diagnostic tools dir is " + diagnosticToolsPath);
            variables.Add("diagnosticToolsPath", diagnosticToolsPath);
            variables.Add("TEMP", DaaS.Infrastructure.Settings.TempDir);

            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");

            variables.Add("ProgramFiles", programFiles);
            variables.Add("ProgramFiles(x86)", programFilesX86);

            foreach (var v in variables)
            {
                string unexpanded = "%" + v.Key + "%";
                if (input.Contains(unexpanded))
                {
                    input = input.Replace(unexpanded, v.Value);
                }
            }

            return input;
        }
        protected Task RunProcessAsync(string command, string args, string sessionId, string description = "")
        {
            return RunProcessAsync(command, args, sessionId, description, CancellationToken.None);
        }
        protected async Task RunProcessAsync(string command, string args, string sessionId, string description, CancellationToken token)
        {
            try
            {
                command = ExpandVariables(command);

                Logger.LogSessionVerboseEvent($"Calling {Name}: {command} {args}", sessionId);

                var toolProcess = DaaS.Infrastructure.RunProcess(command, args, sessionId, description);

                double secondsWaited = 0;
                var processStartTime = DateTime.UtcNow;
                while (!toolProcess.HasExited)
                {
                    await Task.Delay(5000);

                    Logger.LogDiagnostic($"Waiting for process {command} {args} to finish");

                    secondsWaited += 5;
                    if (secondsWaited > 120)
                    {
                        secondsWaited = 0;
                        Logger.LogSessionVerboseEvent($"Waiting for process {command} {args} to finish. Process running for {DateTime.UtcNow.Subtract(processStartTime).TotalSeconds} seconds", sessionId);
                    }

                    if (token != CancellationToken.None && token.IsCancellationRequested)
                    {
                        Logger.LogSessionVerboseEvent($"Kill tool process [{command} {args}] because cancellation is requested", sessionId);
                        try
                        {
                            toolProcess.Kill();
                            Logger.LogSessionVerboseEvent($"Tool process [{command} {args}] killed because cancellation is requested", sessionId);
                        }
                        catch (Exception)
                        {
                            //no-op
                        }

                        token.ThrowIfCancellationRequested();
                    }
                }


                Logger.LogDiagnostic("Exit Code: {0}", toolProcess.ExitCode);
                if (toolProcess.ExitCode != 0)
                {
                    var errorCode = toolProcess.ExitCode;
                    toolProcess.Dispose();
                    var errorMessage = $"Process {command} exited with error code {errorCode}";
                    Logger.LogDiagnostic("Throwing DiagnosticToolError: " + errorMessage);
                    throw new DiagnosticToolErrorException(errorMessage);
                }
            }
            catch (AggregateException ae)
            {
                foreach (var e in ae.InnerExceptions)
                {
                    if (e is ApplicationException)
                    {
                        Logger.LogDiagnostic(e.Message);
                    }
                }
                throw;
            }
        }

        protected string ConvertBackSlashesToForwardSlashes(string logPath, string rootPath = "")
        {
            string relativePath = Path.Combine(rootPath, logPath);
            relativePath = relativePath.Replace('\\', '/');
            return relativePath.TrimStart('/');
        }

        // https://stackoverflow.com/questions/882686/non-blocking-file-copy-in-c-sharp
        protected async Task MoveFileAsync(string sourceFile, string destinationFile, string sessionId, bool deleteAfterCopy)
        {
            try
            {
                FileSystemHelpers.EnsureDirectory(Path.GetDirectoryName(destinationFile));
                Logger.LogSessionVerboseEvent($"Copying file from {sourceFile} to {destinationFile}", sessionId);

                using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    using (var destinationStream = new FileStream(destinationFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        await sourceStream.CopyToAsync(destinationStream);
                    }
                }

                Logger.LogSessionVerboseEvent($"File copied from {sourceFile} to {destinationFile}", sessionId);

                if (deleteAfterCopy)
                {
                    FileSystemHelpers.DeleteFileSafe(sourceFile);
                }
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Failed while copying file", ex, sessionId);
            }
        }
    }
}
