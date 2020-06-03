using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Leases;

namespace DaaS.Diagnostics
{
    abstract class DiagnosticTool
    {
        public virtual string Name { get; internal set; }
        public string Warning { get; internal set; }

        protected static string ExpandVariables(string input, Dictionary<string, string> variables = null)
        {
            if (variables == null)
            {
                variables = new Dictionary<string, string>();
            }

            var diagnosticToolsPath = Infrastructure.Settings.GetDiagnosticToolsPath();
            
            Logger.LogDiagnostic("Diagnostic tools dir is " + diagnosticToolsPath);
            variables.Add("diagnosticToolsPath", diagnosticToolsPath);
            variables.Add("TEMP", Infrastructure.Settings.TempDir);

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
        protected Task RunProcessWhileKeepingLeaseAliveAsync(Lease lease, string command, string args, string sessionId)
        {
            return RunProcessWhileKeepingLeaseAliveAsync(lease, command, args, sessionId, CancellationToken.None);
        }
        protected async Task RunProcessWhileKeepingLeaseAliveAsync(Lease lease, string command, string args, string sessionId, CancellationToken token)
        {
            try
            {
                command = ExpandVariables(command);

                Logger.LogDiagnostic("Calling {0}: {1} {2}", Name, command, args);
                // Call Diagnostic tool
                var toolProcess = Infrastructure.RunProcess(command, args, sessionId);

                double secondsWaited = 0;
                var processStartTime = DateTime.UtcNow;
                while (!toolProcess.HasExited)
                {
                    // We don't want the lease to expire while we're waiting for the collector to finish running
                    await Task.Delay(Infrastructure.Settings.LeaseRenewalTime);
                    lease.Renew();

                    secondsWaited = secondsWaited + Infrastructure.Settings.LeaseRenewalTime.TotalSeconds;
                    if (secondsWaited > 120)
                    {
                        secondsWaited = 0;
                        Logger.LogSessionVerboseEvent($"Waiting for process {command} {args} to finish. Process running for {DateTime.UtcNow.Subtract(processStartTime).TotalSeconds} seconds", sessionId);
                    }

                    if (token != CancellationToken.None)
                    {
                        if (token.IsCancellationRequested)
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
                }

                //var errorStream = toolProcess.StandardError;
                //var errorText = errorStream.ReadToEnd();
                //var outputStream = toolProcess.StandardOutput;
                //var outputText = outputStream.ReadToEnd();

                Logger.LogDiagnostic("Exit Code: {0}", toolProcess.ExitCode);
                //Logger.LogDiagnostic("Error Text: {0}", errorText);
                //Logger.LogDiagnostic("Output Text: {0}", outputText);

                //if (!string.IsNullOrEmpty(errorText))
                if (toolProcess.ExitCode != 0)
                {
                    var errorCode = toolProcess.ExitCode;
                    toolProcess.Dispose();
                    var errorMessage = String.Format(
                        "Process {0} exited with error code {1}.", //" Error message: {2}",
                        command,
                        errorCode); //,
                        //string.IsNullOrWhiteSpace(errorText) ? outputText : errorText);
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
    }
}
