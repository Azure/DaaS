//-----------------------------------------------------------------------
// <copyright file="DaasEventSource.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Sessions;

namespace DaaS
{
    [EventSource(Name = "Microsoft-Azure-AppService-DaaS")]
    public sealed class DaasEventSource : EventSource
    {
        /// <summary>
        /// ETW provider instance.
        /// </summary>
        public static readonly DaasEventSource Instance = new DaasEventSource();

        [Event(1000, Level = EventLevel.Informational)]
        public void LogNewSession(string SiteName, string Version, string SessionId, string Mode, string Diagnosers, string Details)
        {
            WriteEvent(1000, SiteName, Version, SessionId, Mode, Diagnosers, Details);
        }

        [Event(1001, Level = EventLevel.Verbose)]
        public void LogSessionVerboseEvent(string SiteName, string Version, string SessionId, string ActivityId, string Message)
        {
            WriteEvent(1001, SiteName, Version, SessionId, ActivityId, Message);
        }

        [Event(1002, Level = EventLevel.Error)]
        public void LogSessionErrorEvent(string SiteName, string Version, string SessionId, string Message, string ExceptionType, string ExceptionMessage, string ExceptionStackTrace)
        {
            WriteEvent(1002, SiteName, Version, SessionId, Message, ExceptionType, ExceptionMessage, ExceptionStackTrace);
        }

        [Event(1003, Level = EventLevel.Error, Version = 2)]
        public void LogErrorEvent(string SiteName, string Version, string Message, string ExceptionType, string ExceptionMessage, string ExceptionStackTrace, string Details)
        {
            WriteEvent(1003, SiteName, Version, Message, ExceptionType, ExceptionMessage, ExceptionStackTrace, Details);
        }

        [Event(1004, Level = EventLevel.Verbose, Version = 2)]
        public void LogVerboseEvent(string SiteName, string Version, string Message, string Details)
        {
            WriteEvent(1004, SiteName, Version, Message, Details);
        }

        [Event(1005, Level = EventLevel.Informational)]
        public void LogDaasConsoleEvent(string SiteName, string Version, string Message, string Details)
        {
            WriteEvent(1005, SiteName, Version, Message, Details);
        }

        [Event(1006, Level = EventLevel.Warning)]
        public void LogWarningEvent(string SiteName, string Version, string Message, string ExceptionType, string ExceptionMessage, string ExceptionStackTrace, string Details)
        {
            WriteEvent(1004, SiteName, Version, Message, ExceptionType, ExceptionMessage, ExceptionStackTrace, Details);
        }

        [Event(2000, Level = EventLevel.Informational)]
        public void LogNewCpuMonitoringSession(string SiteName, string Version, string SessionId, string Mode, string Details)
        {
            WriteEvent(2000, SiteName, Version, SessionId, Mode, Details);
        }

        [Event(2002, Level = EventLevel.Error)]
        public void LogCpuMonitoringErrorEvent(string SiteName, string Version, string SessionId, string Message, string ExceptionType, string ExceptionMessage, string ExceptionStackTrace)
        {
            WriteEvent(2002, SiteName, Version, SessionId, Message, ExceptionType, ExceptionMessage, ExceptionStackTrace);
        }

        [Event(2003, Level = EventLevel.Verbose)]
        public void LogCpuMonitoringVerboseEvent(string SiteName, string Version, string SessionId, string ActivityId, string Message)
        {
            WriteEvent(2003, SiteName, Version, SessionId, ActivityId, Message);
        }

        [Event(2004, Level = EventLevel.Informational)]
        public void LogCpuMonitoringEvent(string SiteName, string Version, string SessionId, string ActivityId, string Message)
        {
            WriteEvent(2004, SiteName, Version, SessionId, ActivityId, Message);
        }

        [Event(3000, Level = EventLevel.Informational)]
        public void LogDiagnoserStats(string SiteName, string Version, string SessionId, string ActivityId, string Diagnoser, string Details)
        {
            WriteEvent(3000, SiteName, Version, SessionId, ActivityId, Diagnoser, Details);
        }

        [Event(3001, Level = EventLevel.Warning)]
        public void LogDiagnoserWarningEvent(string SiteName, string Version, string SessionId, string ActivityId, string Diagnoser, string Message, string ExceptionType, string ExceptionMessage, string ExceptionStackTrace)
        {
            WriteEvent(3001, SiteName, Version, SessionId, ActivityId, Diagnoser, Message, ExceptionType, ExceptionMessage, ExceptionStackTrace);
        }

        [Event(3002, Level = EventLevel.Error)]
        public void LogDiagnoserErrorEvent(string SiteName, string Version, string SessionId, string ActivityId, string Diagnoser, string Message, string ExceptionType, string ExceptionMessage, string ExceptionStackTrace)
        {
            WriteEvent(3002, SiteName, Version, SessionId, ActivityId, Diagnoser, Message, ExceptionType, ExceptionMessage, ExceptionStackTrace);
        }

        [Event(3003, Level = EventLevel.Verbose)]
        public void LogDiagnoserVerboseEvent(string SiteName, string Version, string SessionId, string ActivityId, string Diagnoser, string Message)
        {
            WriteEvent(3003, SiteName, Version, SessionId, ActivityId, Diagnoser, Message);
        }

        [Event(3004, Level = EventLevel.Informational)]
        public void LogDiagnoserEvent(string SiteName, string Version, string SessionId, string ActivityId, string Diagnoser, string Message)
        {
            WriteEvent(3004, SiteName, Version, SessionId, ActivityId, Diagnoser, Message);
        }

        [Event(3005, Level = EventLevel.Verbose)]
        public void LogApiStatus(string SiteName, string Version, string Message, string Path, string Method, int StatusCode, long LatencyInMilliseconds, string Details)
        {
            WriteEvent(3005, SiteName, Version, Message, Path, Method, StatusCode, LatencyInMilliseconds, Details);
        }
    }
}
