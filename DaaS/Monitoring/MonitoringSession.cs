//-----------------------------------------------------------------------
// <copyright file="MonitoringSession.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DaaS
{
    public enum AnalysisStatus
    {
        NotStarted,
        InProgress,
        Completed
    }
    public enum SessionMode
    {
        Kill,
        Collect,
        CollectAndKill,
        CollectKillAndAnalyze
    }
    public class MonitoringSession
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public SessionMode Mode { get; set; }
        public string SessionId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string ProcessesToMonitor { get; set; }
        public bool MonitorScmProcesses { get; set; }
        public int CpuThreshold { get; set; }
        public int ThresholdSeconds { get; set; }
        public int MonitorDuration { get; set; }
        public string ActionToExecute { get; set; }
        public string ArgumentsToAction { get; set; }
        public int MaxActions { get; set; }
        public int MaximumNumberOfHours { get; set; }
        public string BlobSasUri { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public AnalysisStatus AnalysisStatus { get; set; }
        public List<MonitoringFile> FilesCollected { get; set; }
    }
    

    public class ActiveMonitoringSession
    {
        public MonitoringSession Session { get; set; }
        public List<MonitoringLogsPerInstance> MonitoringLogs { get; set; }
    }
}
