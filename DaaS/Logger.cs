// -----------------------------------------------------------------------
// <copyright file="Logger.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Azure;
using DaaS.Diagnostics;
using Microsoft.Azure.Storage;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace DaaS
{
    public static class Logger
    {
        private const int MaxExceptionDepthToLog = 5;

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

            //
            // Some diagnosers may not pass the output directory
            //

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }

            if (collectorMode)
            {
                var listener = new TextWriterTraceListener(Path.Combine(outputPath, $"{Environment.MachineName}_{CallerComponent}.diaglog"), "TextWriterTraceListener")
                {
                    TraceOutputOptions = TraceOptions.None
                };

                Trace.Listeners.Add(listener);

                string statusFilePath = string.Empty;
                string localTemp = EnvironmentVariables.LocalTemp.ToLower();
                string daasPath = EnvironmentVariables.DaasPath.ToLower();
                try
                {
                    statusFilePath = outputPath.ToLower().Replace(localTemp, daasPath);
                    LogVerboseEvent($"statusFilePath = {statusFilePath}");

                    if (!Directory.Exists(statusFilePath))
                    {
                        Directory.CreateDirectory(statusFilePath);
                    }

                    if (!Directory.Exists(outputPath))
                    {
                        Directory.CreateDirectory(outputPath);
                    }

                    StatusFile = Path.Combine(statusFilePath, "diagstatus.diaglog");
                    ErrorFilePath = Path.Combine(outputPath, $"{Environment.MachineName}_{CallerComponent}.err.diaglog");
                }
                catch (Exception ex)
                {
                    LogSessionErrorEvent($"Failed while setting StatusFile = {statusFilePath}, outputPath = {outputPath}, localTemp ={localTemp}, daasPath ={daasPath}", ex, DaasSessionId);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(outputPath, "logs"));
                var listener = new TextWriterTraceListener(Path.Combine(outputPath, "logs", $"{Environment.MachineName}_{CallerComponent}.diaglog"), "TextWriterTraceListener")
                {
                    TraceOutputOptions = TraceOptions.None
                };

                Trace.Listeners.Add(listener);

                string statusFilePath = string.Empty;
                string localTemp = EnvironmentVariables.LocalTemp.ToLower();
                string daasPath = EnvironmentVariables.DaasPath.ToLower();
                try
                {
                    statusFilePath = outputPath.ToLower().Replace(localTemp, daasPath);
                    if (!Directory.Exists(statusFilePath))
                    {
                        Directory.CreateDirectory(statusFilePath);
                    }
                    StatusFile = Path.Combine(statusFilePath, Path.GetFileName(inputFile) + ".diagstatus.diaglog");
                    ErrorFilePath = Path.Combine(outputPath, Path.GetFileName(inputFile) + ".err.diaglog");
                }
                catch (Exception ex)
                {
                    LogSessionErrorEvent($"Failed while setting StatusFile, statusFilePath ={statusFilePath}, localTemp = {localTemp}, daasPath = {daasPath}", ex, DaasSessionId);
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
                monitoringSession.RuleType
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

        public static void LogVerboseEvent(string message, string details = "")
        {
            try
            {
                LogInfo(message);
                DaasEventSource.Instance.LogVerboseEvent(SiteName, _assemblyVersion, message, details);
            }
            catch (Exception)
            {
            }
        }

        public static void LogDaasConsoleEvent(string message, string details, string sessionId)
        {
            DaasEventSource.Instance.LogDaasConsoleEvent(SiteName, _assemblyVersion, message, details, sessionId);
            LogDiagnostic("DaasConsole:{0} {1}", message, details);
        }

        public static void LogErrorEvent(string message, string exceptionType, string exceptionMessage, string exceptionStackTrace, string details)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(CallerComponent))
                {
                    message = $"{CallerComponent}: {message}";
                }
                Trace.TraceError($"{DateTime.UtcNow } {message} {exceptionType}:{exceptionMessage} {details} {exceptionStackTrace}");
                DaasEventSource.Instance.LogErrorEvent(SiteName, _assemblyVersion, message, exceptionType, exceptionMessage, exceptionStackTrace, details);
            }
            catch (Exception)
            {
            }
        }

        public static void LogErrorEvent(string message, Exception exception)
        {
            var details = GetExceptionDetails(exception);
            LogErrorEvent(message, exception.GetType().ToString(), exception.Message, exception.StackTrace, details);
        }

        private static string GetStorageExceptionDetails(StorageException storageException)
        {
            try
            {
                if (storageException.RequestInformation != null
                && storageException.RequestInformation.ExtendedErrorInformation != null)
                {
                    return "ExtendedErrorInformation = " + JsonConvert.SerializeObject(storageException.RequestInformation.ExtendedErrorInformation);
                }
            }
            catch (Exception)
            {
            }

             return string.Empty;
        }

        private static string GetRequestFailedExceptionDetails(RequestFailedException requestFailedException)
        {
            try
            {
                return $"ErrorCode = { requestFailedException.ErrorCode} , Status = {requestFailedException.Status}";
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        private static string GetExceptionDetails(Exception exception)
        {
            var builder = new StringBuilder();
            if (exception is StorageException storageEx) 
            { 
                builder.AppendLine(GetStorageExceptionDetails(storageEx));
            }
            else if (exception is RequestFailedException requestFailedEx)
            {
                builder.AppendLine(GetRequestFailedExceptionDetails(requestFailedEx));
            }
            else if (exception is DiagnosticSessionAbortedException diagnosticSessionAbortedException 
                && diagnosticSessionAbortedException.InnerException != null)
            {
                if (diagnosticSessionAbortedException.InnerException is StorageException storageException)
                {
                    builder.AppendLine(GetStorageExceptionDetails(storageException));
                }

                if (diagnosticSessionAbortedException.InnerException is RequestFailedException requestFailedException)
                {
                    builder.AppendLine(GetRequestFailedExceptionDetails(requestFailedException));
                }
            }

            else if (exception is AggregateException aggregateException)
            {
                aggregateException.Handle((x) =>
                {
                    builder.AppendLine(x.ToString());
                    return true;
                });
            }

            int currentDepth = 0;
            while (exception.InnerException != null && currentDepth < MaxExceptionDepthToLog)
            {
                builder.AppendLine($"Inner Exception {currentDepth + 1} = {exception}");
                exception = exception.InnerException;
                ++currentDepth;
            }

            return builder.ToString();
        }

        public static void LogWarningEvent(string message, Exception exception, string details = "")
        {
            LogWarningEvent(message, exception.GetType().ToString(), exception.Message, exception.StackTrace, details);
        }

        private static void LogWarningEvent(string message, string exceptionType, string exceptionMessage, string exceptionStackTrace, string details)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(CallerComponent))
                {
                    message = $"{CallerComponent}: {message}";
                }
                Trace.TraceWarning($"{DateTime.UtcNow } {message} {exceptionType}:{exceptionMessage} {details} {exceptionStackTrace}");
                DaasEventSource.Instance.LogWarningEvent(SiteName, _assemblyVersion, message, exceptionType, exceptionMessage, exceptionStackTrace, details);
            }
            catch (Exception)
            {
            }
        }

        public static void LogErrorEvent(string message, string exception)
        {
            LogErrorEvent(message, exceptionType: string.Empty, exceptionMessage: exception, string.Empty, string.Empty);
            LogDiagnostic("[ERR] - {0} {1}", message, exception);
        }

        public static void LogSessionErrorEvent(string message, Exception ex, string sessionId)
        {
            var details = GetExceptionDetails(ex);
            DaasEventSource.Instance.LogSessionErrorEvent(SiteName, _assemblyVersion, sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace, details);
            LogDiagnostic("Session [ERR] - {0} {1} {2} {3} {4}", sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
        }

        public static void LogSessionErrorEvent(string message, string error, string sessionId, string details = "")
        {
            DaasEventSource.Instance.LogSessionErrorEvent(SiteName, _assemblyVersion, sessionId, message,ExceptionType:"", error, ExceptionStackTrace:"", Details:details);
            LogDiagnostic("Session [ERR] - {0} {1} {2} {3}", sessionId, message, error, details);
        }

        public static void LogSessionWarningEvent(string message, Exception ex, string sessionId)
        {
            var details = GetExceptionDetails(ex);
            DaasEventSource.Instance.LogSessionWarningEvent(SiteName, _assemblyVersion, sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace, details);
            LogDiagnostic("Session [WARN] - {0} {1} {2} {3} {4}", sessionId, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
        }

        public static void LogSessionWarningEvent(string message, string error, string sessionId)
        {
            DaasEventSource.Instance.LogSessionWarningEvent(SiteName, _assemblyVersion, sessionId, message, string.Empty, error, string.Empty, string.Empty);
            LogDiagnostic("Session [WARN] - {0} {1} {2}", sessionId, message, error);
        }

        public static void LogNewSession(string sessionId, string mode, string diagnosers, object details)
        {
            DaasEventSource.Instance.LogNewSession(SiteName, _assemblyVersion, sessionId, mode, diagnosers, JsonConvert.SerializeObject(details));
            LogDiagnostic("New Session - {0} {1} {2} {3}", sessionId, mode, diagnosers, JsonConvert.SerializeObject(details));
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
            Console.WriteLine(message);
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

        public static void LogDiagnoserWarningEvent(string message, Exception ex)
        {
            DaasEventSource.Instance.LogDiagnoserWarningEvent(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
            LogDiagnostic("Diagnoser [WARN] {0} {1} {2} {3} {4} {5} {6}", DaasSessionId, ActivityId.ToString(), CallerComponent, message, ex.GetType().ToString(), ex.Message, ex.StackTrace);
        }
        public static void LogDiagnoserWarningEvent(string message, string exceptionMessage)
        {
            DaasEventSource.Instance.LogDiagnoserWarningEvent(SiteName, _assemblyVersion, DaasSessionId, ActivityId.ToString(), CallerComponent, message, string.Empty, exceptionMessage, string.Empty);
            LogDiagnostic("Diagnoser [WARN] {0} {1} {2} {3} {4}", DaasSessionId, ActivityId.ToString(), CallerComponent, message, exceptionMessage);
        }

        public static void LogApiStatus(string path, string method, int statusCode, long latencyInMilliseconds, string responseWhenError)
        {
            DaasEventSource.Instance.LogApiStatus(SiteName, _assemblyVersion, "API Call", path, method, statusCode, latencyInMilliseconds, responseWhenError);
        }
    }
}
