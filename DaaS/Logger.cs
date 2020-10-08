//-----------------------------------------------------------------------
// <copyright file="Logger.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DaaS
{
    public static class Logger
    {
        public static string StatusFile = "";
        public static string ErrorFilePath = "";
        private static string CallerComponent;
        private static readonly string _assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() + "." + Assembly.GetExecutingAssembly().GetName().Version.Minor.ToString();

        public static string SiteName { get; private set; } = (Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME") == null ? "SiteNotFound" : (Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME").StartsWith("~1") ? Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME").Substring(2) : Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME")));
        public static Guid ActivityId { get; set; } = Guid.Empty;

        public static string DaasSessionId { get; private set; } = string.Empty;

        public static bool KustoLoggingDisabled { get; set; }

        public static void Init(string inputFile, string outputPath, string callerComponent, bool collectorMode)
        {
            CallerComponent = callerComponent;
            DaasSessionId = Environment.GetEnvironmentVariable("DAAS_SESSION_ID") ?? "";
            ActivityId = Guid.NewGuid();

            Trace.UseGlobalLock = false;
            Trace.AutoFlush = true;
            Trace.IndentSize = 4;

            if (collectorMode)
            {
                var listener = new TextWriterTraceListener(Path.Combine(outputPath, $"{Environment.MachineName}_{CallerComponent}.diaglog"), "TextWriterTraceListener")
                {
                    TraceOutputOptions = TraceOptions.None
                };

                Trace.Listeners.Add(listener);

                try
                {
                    string statusFilePath = Path.GetDirectoryName(outputPath).ToLower().Replace(EnvironmentVariables.LocalTemp.ToLower(), EnvironmentVariables.DaasPath.ToLower());
                    Directory.CreateDirectory(statusFilePath);
                    StatusFile = Path.Combine(statusFilePath, "diagstatus.diaglog");
                    ErrorFilePath = Path.Combine(outputPath, $"{Environment.MachineName}_{CallerComponent}.err.diaglog");
                }
                catch (Exception ex)
                {
                    LogSessionErrorEvent("Failed while setting StatusFile", ex, DaasSessionId);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    Directory.CreateDirectory(Path.Combine(outputPath, "logs"));
                    var listener = new TextWriterTraceListener(Path.Combine(outputPath, "logs", $"{Environment.MachineName}_{CallerComponent}.diaglog"), "TextWriterTraceListener")
                    {
                        TraceOutputOptions = TraceOptions.None
                    };

                    Trace.Listeners.Add(listener);

                    try
                    {
                        string statusFilePath = outputPath.ToLower().Replace(EnvironmentVariables.LocalTemp.ToLower(), EnvironmentVariables.DaasPath.ToLower());
                        if (!Directory.Exists(statusFilePath))
                        {
                            Directory.CreateDirectory(statusFilePath);
                        }
                        StatusFile = Path.Combine(statusFilePath, Path.GetFileName(inputFile) + ".diagstatus.diaglog");
                        ErrorFilePath = Path.Combine(outputPath, Path.GetFileName(inputFile) + ".err.diaglog");
                    }
                    catch (Exception ex)
                    {
                        LogSessionErrorEvent("Failed while setting StatusFile", ex, DaasSessionId);
                    }
                }
            }

        }

        internal static void LogNewCpuMonitoringSession(MonitoringSession monitoringSession)
        {
            var details = new
            {
                monitoringSession.CpuThreshold,
                monitoringSession.ThresholdSeconds,
                monitoringSession.MonitorScmProcesses,
                monitoringSession.MaxActions,
                monitoringSession.MaximumNumberOfHours,
                monitoringSession.BlobStorageHostName,
                SasUriEnvironmentVariableExists = Configuration.Settings.IsBlobSasUriConfiguredAsEnvironmentVariable()
            };

            DaasEventSource.Instance.LogNewCpuMonitoringSession(SiteName, _assemblyVersion, monitoringSession.SessionId, monitoringSession.Mode.ToString(), JsonConvert.SerializeObject(details));
            LogDiagnostic("New CPU Monitoring Session submitted {0}", JsonConvert.SerializeObject(details));
        }

        public static void LogDiagnostic(string format, params object[] arg)
        {
            try
            {
                if (bool.TryParse(Environment.GetEnvironmentVariable("DAAS_DEBUG"), out bool debugMode) && debugMode)
                {
                    string message = $"{DateTime.UtcNow } [[{_assemblyVersion}]] [{Environment.MachineName}] {string.Format(format, arg)}";
                    Trace.TraceInformation(message);
                }
            }
            catch (Exception)
            {
            }
        }

        public static void LogInfo(string logMessage)
        {
            try
            {
                logMessage = $"{DateTime.UtcNow } [[{_assemblyVersion}]] [{Environment.MachineName}] {logMessage}";
                Trace.TraceInformation(logMessage);
            }
            catch (Exception)
            {
            }
        }

        public static void LogVerboseEvent(string message)
        {
            try
            {
                LogInfo(message);
                DaasEventSource.Instance.LogVerboseEvent(SiteName, _assemblyVersion, message);
            }
            catch (Exception)
            {
            }
        }

        public static void LogDaasConsoleEvent(string message, string details)
        {
            DaasEventSource.Instance.LogDaasConsoleEvent(SiteName, _assemblyVersion, message, details);
            LogDiagnostic("DaasConsole:{0} {1}", message, details);
        }

        public static void LogErrorEvent(string message, string exceptionType, string exceptionMessage, string exceptionStackTrace)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(CallerComponent))
                {
                    message = $"{CallerComponent}: {message}";
                }
                Trace.TraceError($"{DateTime.UtcNow } {message} {exceptionType}:{exceptionMessage} {exceptionStackTrace}");
                DaasEventSource.Instance.LogErrorEvent(SiteName, _assemblyVersion, message, exceptionType, exceptionMessage, exceptionStackTrace);
            }
            catch (Exception)
            {
            }
        }

        public static void LogErrorEvent(string message, Exception exception)
        {
            LogErrorEvent(message, exception.GetType().ToString(), exception.Message, exception.StackTrace);
        }
        public static void LogErrorEvent(string message, string exception)
        {
            LogErrorEvent(message, exception, string.Empty, string.Empty);
            LogDiagnostic("[ERR] - {0} {1}", message, exception);
        }

        public static void LogSessionErrorEvent(string message, Exception ex, string sessionId)
        {
            DaasEventSource.Instance.LogSessionErrorEvent(SiteName, _assemblyVersion, sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
            LogDiagnostic("Session [ERR] - {0} {1} {2} {3} {4}", sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
        }

        public static void LogNewSession(string sessionId, string mode, string diagnosers, string Instances, bool invokedViaDaasConsole, bool hasblobSasUri, bool sasUriInEnvironmentVariable)
        {
            var details = new
            {
                Instances,
                invokedViaDaasConsole,
                hasblobSasUri,
                sasUriInEnvironmentVariable
            };

            DaasEventSource.Instance.LogNewSession(SiteName, _assemblyVersion, sessionId, mode, diagnosers, JsonConvert.SerializeObject(details));
            LogDiagnostic("New Session - {0} {1} {2} {3}", sessionId , mode, diagnosers, JsonConvert.SerializeObject(details));
        }

        public static void TraceFatal(string message, bool logErrorTrace = true)
        {
            if (!string.IsNullOrWhiteSpace(ErrorFilePath))
            {
                using (StreamWriter file = File.CreateText(ErrorFilePath))
                {
                    file.WriteLine(message);
                }
            }

            if (logErrorTrace)
            {
                LogErrorEvent("FATAL Error", message);
            }
        }

        public static void TraceStats(string message)
        {
            if (!KustoLoggingDisabled)
            {
                DaasEventSource.Instance.LogDiagnoserStats(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message);
            }
        }

        public static void LogStatus(string message)
        {
            LogDiagnoserEvent(message);
            if (!string.IsNullOrWhiteSpace(StatusFile))
            {
                try
                {
                    using (FileStream fs = new FileStream(StatusFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        using (StreamWriter sw = new StreamWriter(fs))
                        {
                            sw.WriteLine(message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // let's not slow down the collector by doing a retry
                    // for the status file. Just log for now and we will 
                    // see if we get too many of these exceptions
                    LogSessionErrorEvent("Failed while writing to StatusFile", ex, DaasSessionId);
                }
            }
        }

        public static void LogSessionVerboseEvent(string message, string sessionId)
        {
            DaasEventSource.Instance.LogSessionVerboseEvent(SiteName, _assemblyVersion, sessionId, ActivityId.ToString(), message);
            LogDiagnostic("Session [VERB] {0} {1} {2}", sessionId, ActivityId.ToString(), message);
        }

        public static void LogCpuMonitoringVerboseEvent(string message, string sessionId)
        {
            DaasEventSource.Instance.LogCpuMonitoringVerboseEvent(SiteName, _assemblyVersion, sessionId, ActivityId.ToString(), message);
            LogDiagnostic("CPUMonitoring [VERB] {0} {1} {2}", sessionId, ActivityId.ToString(), message);
        }

        public static void LogCpuMonitoringEvent(string message, string sessionId)
        {
            DaasEventSource.Instance.LogCpuMonitoringEvent(SiteName, _assemblyVersion, sessionId, ActivityId.ToString(), message);
            LogDiagnostic("CPUMonitoring [INF] {0} {1} {2}", sessionId, ActivityId.ToString(), message);
        }

        public static void LogCpuMonitoringErrorEvent(string message, Exception ex, string sessionId)
        {
            DaasEventSource.Instance.LogCpuMonitoringErrorEvent(SiteName, _assemblyVersion, sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
            LogDiagnostic("CPUMonitoring [ERR] {0} {1} {2} {3} {4}", sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
        }

        public static void LogDiagnoserVerboseEvent(string message)
        {
            DaasEventSource.Instance.LogDiagnoserVerboseEvent(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message);
            LogDiagnostic("Diagnoser [VERB] {0} {1} {2} {3}", DaasSessionId, ActivityId.ToString(), CallerComponent, message);
        }
        public static void LogDiagnoserEvent(string message)
        {
            DaasEventSource.Instance.LogDiagnoserEvent(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message);
            LogDiagnostic("Diagnoser [INF] {0} {1} {2} {3}", DaasSessionId, ActivityId.ToString(), CallerComponent, message);
        }

        public static void LogDiagnoserErrorEvent(string message, Exception ex)
        {
            DaasEventSource.Instance.LogDiagnoserErrorEvent(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
            LogDiagnostic("Diagnoser [ERR] {0} {1} {2} {3} {4} {5} {6}", DaasSessionId, ActivityId.ToString(), CallerComponent, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
        }
        public static void LogDiagnoserErrorEvent(string message, string exceptionMessage)
        {
            DaasEventSource.Instance.LogDiagnoserErrorEvent(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message, string.Empty, exceptionMessage, string.Empty);
            LogDiagnostic("Diagnoser [ERR] {0} {1} {2} {3} {4}", DaasSessionId, ActivityId.ToString(), CallerComponent, message, exceptionMessage);
        }

        public static void LogApiStatus(string path, string method, int statusCode, long latencyInMilliseconds, string responseWhenError)
        {
            DaasEventSource.Instance.LogApiStatus(SiteName, _assemblyVersion, "API Call", path, method, statusCode, latencyInMilliseconds, responseWhenError);
        }
    }
}
