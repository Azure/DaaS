// -----------------------------------------------------------------------
// <copyright file="ProfileManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClrProflingCollector
{
    internal static class ProfileManager
    {
        private const string UserModeCustomProviderAgentGuid = "F5091AA9-80DC-49FF-A7CF-BD1103FE149D";
        private const string DetailedTracingAgentGuid = "31003EE3-A8E1-427A-931E-97057D4D2B7D";
        private const string IisWebServerProviderGuid = "3A2A4E84-4C21-4981-AE10-3FDA0D9B0F83";
        private const string DiagnosticsHubAgentGuid = "4EA90761-2248-496C-B854-3C0399A591A4";
        private const string AspNetProviderGuid = "AFF081FE-0247-4275-9C4E-021F3DC1DA35";
        private const string MicrosoftExtensionsLoggingProviderGuid = "3AC73B97-AF73-50E9-0822-5DA4367920D0";

        private static ConcurrentDictionary<int, ProfileInfo> _profilingList = new ConcurrentDictionary<int, ProfileInfo>();
        private static object _lockObject = new object();

        private static readonly TimeSpan _maxProfilingDuration = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan _profilingIisDuration = GetIisProfilingDuration();

        private static string _processName;

        static ProfileManager()
        {
            _processName = Environment.ExpandEnvironmentVariables("%SystemDrive%\\Program Files\\Microsoft Visual Studio 15.0\\Team Tools\\DiagnosticsHub\\Collector\\VSDiagnostics.exe");
        }
        public static string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
        internal static ProfileResultInfo StartProfile(int processId, bool iisProfiling = true, int[] additionaProcessIds = null)
        {
            Logger.LogInfo("ProfileManager.StartProfile");

            // Check if the profiling is already running for the given process. If it does, then just return with 200.
            if (_profilingList.ContainsKey(processId))
            {
                return new ProfileResultInfo(HttpStatusCode.OK, string.Empty);
            }

            int profilingSessionId = GetNextProfilingSessionId();
            ProfileResultInfo profileProcessResponse = null;

            if (iisProfiling)
            {
                profileProcessResponse = StartIisSession(processId, profilingSessionId, additionaProcessIds);
            }
            else
            {
                string arguments = Environment.ExpandEnvironmentVariables(string.Format("start {0} /attach:{1} /loadAgent:{2};DiagnosticsHub.CpuAgent.dll;{{\\\"collectNgenPdbs\\\":false}}  /scratchLocation:%LOCAL_EXPANDED%\\Temp", profilingSessionId, processId, DiagnosticsHubAgentGuid));
                profileProcessResponse = ExecuteProfilingCommand(arguments);
            }

            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                return profileProcessResponse;
            }

            // This may fail if we got 2 requests at the same time to start a profiling session
            // in that case, only 1 will be added and the other one will be stopped.
            if (!_profilingList.TryAdd(processId, new ProfileInfo(profilingSessionId, true)))
            {
                Logger.LogDiagnoserVerboseEvent(string.Format("WARNING:A profiling session was already running for process {0}, stopping profiling session {1}", processId, profilingSessionId));
                StopProfileInternal(processId, profilingSessionId);
                return new ProfileResultInfo(HttpStatusCode.OK, string.Empty);
            }

            Logger.LogInfo(string.Format("started session id: {0} for pid: {1}", profilingSessionId, processId));

            return new ProfileResultInfo(HttpStatusCode.OK, string.Empty);

        }

        private static ProfileResultInfo StartIisSession(int processId, int profilingSessionId, int[] additionaProcessIds = null)
        {
            // Starting a new profiler session with the Detailed Tracing Agent
            string arguments = System.Environment.ExpandEnvironmentVariables(string.Format("start {0} /scratchLocation:\"%LOCAL_EXPANDED%\\Temp\" /loadAgent:{1};ServiceProfilerAgent.dll", profilingSessionId, DetailedTracingAgentGuid));
            var profileProcessResponse = ExecuteProfilingCommand(arguments);
            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                return profileProcessResponse;
            }

            // Attach to the process and enable the Custom ETW Provider agent
            arguments = string.Format("update {0} /attach:{1} /loadAgent:{2};ServiceProfilerAgent.dll", profilingSessionId, processId, UserModeCustomProviderAgentGuid);
            profileProcessResponse = ExecuteProfilingCommand(arguments);
            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                // If we failed here for whatever reason, we should ensure that we
                // call the right command to stop the profiler, else it might be left running
                StopProfile(processId);
                return profileProcessResponse;
            }

            // Adding the IIS WWW Server Provider Events to the profiling session
            arguments = string.Format("postString {0} \"AddProvider:{1}:0xFFFFFFFE:5\" /agent:{2}", profilingSessionId, IisWebServerProviderGuid, UserModeCustomProviderAgentGuid);
            profileProcessResponse = ExecuteProfilingCommand(arguments);
            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                Logger.LogDiagnoserVerboseEvent(string.Format("WARNING:Failed enabling the IIS provider with the message {0} and Status {1}", profileProcessResponse.Message, profileProcessResponse.StatusCode));
            }

            // Adding the ASP.NET Provider to ensure that we capture all events for AspNet
            arguments = string.Format("postString {0} \"AddProvider:{1}:0xFFFFFFFF:5\" /agent:{2}", profilingSessionId, AspNetProviderGuid, UserModeCustomProviderAgentGuid);
            profileProcessResponse = ExecuteProfilingCommand(arguments);
            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                Logger.LogDiagnoserVerboseEvent(string.Format("WARNING:Failed enabling the ASP.NET provider with the message {0} and Status {1}", profileProcessResponse.Message, profileProcessResponse.StatusCode));
            }

            // Adding the Microsoft.Extensions.Logging Provider to ensure that we capture all events for AspNet
            arguments = string.Format("postString {0} \"AddProvider:{1}:0xFFFFFFFFFFFFFFFF:5\" /agent:{2}", profilingSessionId, MicrosoftExtensionsLoggingProviderGuid, UserModeCustomProviderAgentGuid);
            profileProcessResponse = ExecuteProfilingCommand(arguments);
            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                Logger.LogDiagnoserVerboseEvent(string.Format("WARNING:Failed enabling the Microsoft.Extensions.Logging Provider with the message {0} and Status {1}", profileProcessResponse.Message, profileProcessResponse.StatusCode));
            }

            if (additionaProcessIds != null && additionaProcessIds.Length > 0)
            {
                foreach (var childProcessId in additionaProcessIds)
                {
                    arguments = string.Format("update {0} /attach:{1} /loadAgent:{2};ServiceProfilerAgent.dll", profilingSessionId, childProcessId, UserModeCustomProviderAgentGuid);
                    profileProcessResponse = ExecuteProfilingCommand(arguments);
                    if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
                    {
                        Logger.LogDiagnoserVerboseEvent(string.Format("WARNING: For .Net Core app, failed while attaching profiler for childProcesses with Id-{0} with the message {1}  and Status {2}", childProcessId, profileProcessResponse.Message, profileProcessResponse.StatusCode));
                    }
                }
            }

            return profileProcessResponse;
        }

        internal static TimeSpan GetIisProfilingDuration()
        {
            TimeSpan iisProfilingDuration = TimeSpan.FromMinutes(1);

            int timeout = 0;
            string iisProfilingDurationInSeconds = System.Environment.GetEnvironmentVariable("APPSETTING_IIS_PROFILING_TIMEOUT_IN_SECONDS");

            if (Int32.TryParse(iisProfilingDurationInSeconds, out timeout))
            {
                if (timeout <= _maxProfilingDuration.TotalSeconds)
                {
                    iisProfilingDuration = TimeSpan.FromSeconds(timeout);
                }
            }

            return iisProfilingDuration;
        }

        internal static ProfileResultInfo StopProfile(int processId)
        {
            int profilingSessionId;

            Logger.LogInfo("ProfileManager.StopProfile");

            // check if the profiling is running for the given process. If it doesn't return 404.
            if (!_profilingList.ContainsKey(processId))
            {
                return new ProfileResultInfo(HttpStatusCode.NotFound, string.Format("Profiling for process '{0}' is not running.", processId));
            }
            else
            {
                profilingSessionId = _profilingList[processId].SessionId;
            }

            return StopProfileInternal(processId, profilingSessionId);

        }

        internal static string GetProfilePath(int processId)
        {
            // TODO: Hard-coding to w3wp for now as Analyzer will look for
            // w3wp_ in ExtraceProcessIdFromEtlFileName method.
            // change this once we collect other process'es ETW

            var processName = "w3wp";

            try
            {
                var p = Process.GetProcessById(processId);
                processName = p.ProcessName;
            }
            catch (Exception)
            {
            }

            string profileFileName = string.Format("{0}_{1}_{2}_{3}.diagsession", Environment.MachineName, processName, processId, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            return System.Environment.ExpandEnvironmentVariables("%LOCAL_EXPANDED%\\Temp\\" + profileFileName);
        }

        private static ProfileResultInfo StopProfileInternal(int processId, int profilingSessionId)
        {
            string profileFileFullPath = GetProfilePath(processId);
            string profileFileName = Path.GetFileName(profileFileFullPath);
            string arguments = string.Format("stop {0} /output:{1}", profilingSessionId, profileFileFullPath);

            var profileProcessResponse = ExecuteProfilingCommand(arguments);

            ProfileInfo removedId;
            if (profileProcessResponse.StatusCode != HttpStatusCode.OK)
            {
                _profilingList.TryRemove(processId, out removedId);
                return profileProcessResponse;
            }

            EnsureDirectory(Path.GetDirectoryName(profileFileFullPath));
            Logger.LogInfo(string.Format("profile was saved to {0} successfully.", profileFileFullPath));

            _profilingList.TryRemove(processId, out removedId);

            return new ProfileResultInfo(HttpStatusCode.OK, string.Empty, profileFileFullPath);

        }

        private static ProfileResultInfo ExecuteProfilingCommand(string arguments)
        {
            MemoryStream outputStream = null;
            MemoryStream errorStream = null;
            try
            {
                var cancellationTokenSource = new CancellationTokenSource();

                Logger.LogDiagnoserVerboseEvent("Launching Profiler Command - ProcessName:" + _processName + "   arguments:" + arguments);

                Process process = new Process();

                ProcessStartInfo pinfo = new ProcessStartInfo
                {
                    Arguments = arguments,
                    FileName = _processName
                };

                process.StartInfo = pinfo;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                outputStream = new MemoryStream();
                errorStream = new MemoryStream();

                process.Start();

                var tasks = new List<Task>
                {
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardOutput.BaseStream, outputStream, cancellationTokenSource.Token),
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardError.BaseStream, errorStream, cancellationTokenSource.Token)
                };
                process.WaitForExit();

                Task.WhenAll(tasks);

                string output = outputStream.ReadToEnd();
                string error = errorStream.ReadToEnd();

                Logger.LogInfo(output);

                if (!string.IsNullOrEmpty(error))
                {
                    Logger.LogInfo(error);
                }

                if (process.ExitCode != 0)
                {
                    Logger.LogDiagnoserErrorEvent(string.Format(CultureInfo.InvariantCulture, "Starting process {0} failed with the following error code '{1}'.", _processName, process.ExitCode), $"process.ExitCode = {process.ExitCode.ToString()}");
                    return new ProfileResultInfo(HttpStatusCode.InternalServerError, "Profiling process failed with the following error code: " + process.ExitCode);
                }

                else if (!string.IsNullOrEmpty(error))
                {
                    return new ProfileResultInfo(HttpStatusCode.InternalServerError, "Profiling process failed with the following error: " + error);
                }

                return new ProfileResultInfo(HttpStatusCode.OK, string.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Executing Commmand {_processName} {arguments} failed", ex);
                return new ProfileResultInfo(HttpStatusCode.InternalServerError, ex.Message);
            }
            finally
            {
                if (outputStream != null)
                {
                    outputStream.Dispose();
                }

                if (errorStream != null)
                {
                    errorStream.Dispose();
                }
            }
        }

        private static int GetNextProfilingSessionId()
        {
            // TODO: This is not a good way to track active profiling sessions, but as of VS2015 RTM, the profiling service does not provide any API to track the current active sessions.  
            // This is planned to be fixed in VS2015 Update 1.
            var r = new Random();

            int sessionId = r.Next(1, 255);
            var lookup = new HashSet<int>(_profilingList.Values.Select(v => v.SessionId));

            while (lookup.Contains(sessionId))
            {
                sessionId = r.Next(1, 255);
            }

            return sessionId;
        }

        internal class ProfileResultInfo
        {
            public ProfileResultInfo(HttpStatusCode statusCode, string message, string filePath = "")
            {
                this.StatusCode = statusCode;
                this.Message = message;
                this.FilePath = filePath;
            }

            public HttpStatusCode StatusCode { get; set; }

            public string Message { get; set; }

            public string FilePath { get; set; }
        }

        private class ProfileInfo
        {
            public ProfileInfo(int sessionId, bool iisProfiling)
            {
                this.SessionId = sessionId;
                this.StartTime = DateTime.UtcNow;
                this.IsIisProfiling = iisProfiling;
            }

            public int SessionId { get; set; }

            public DateTime StartTime { get; set; }

            public bool IsIisProfiling { get; set; }
        }

    }
}
