// -----------------------------------------------------------------------
// <copyright file="MonitoringSession.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using DaaS.Configuration;
using DaaS.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DaaS
{
    public enum RuleType
    {
        Diagnostics,
        AlwaysOn
    }

    public enum AnalysisStatus
    {
        NotStarted,
        InProgress,
        Completed,
        Continuous
    }
    public enum SessionMode
    {
        Kill,
        Collect,
        CollectAndKill,
        CollectKillAndAnalyze
    }

    public class MonitoringSessionResponse : MonitoringSession
    {
        public MonitoringSessionResponse(MonitoringSession s)
        {
            Mode = s.Mode;
            RuleType = s.RuleType;
            SessionId = s.SessionId;
            StartDate = s.StartDate;
            EndDate = s.EndDate;
            MonitorScmProcesses = s.MonitorScmProcesses;
            CpuThreshold = s.CpuThreshold;
            ThresholdSeconds = s.ThresholdSeconds;
            MonitorDuration = s.MonitorDuration;
            ActionToExecute = s.ActionToExecute;
            ArgumentsToAction = s.ArgumentsToAction;
            MaxActions = s.MaxActions;
            MaximumNumberOfHours = s.MaximumNumberOfHours;
            AnalysisStatus = s.AnalysisStatus;
            FilesCollected = s.FilesCollected;
            BlobStorageHostName = s.BlobStorageHostName;
            IntervalDays = s.IntervalDays;
            ActionsInInterval = s.ActionsInInterval;
            DefaultHostName = s.DefaultHostName;
            ProcessWarmupTime = s.ProcessWarmupTime;
        }

        public string BlobSasUri
        {
            get
            {
                var blobSasUri = Settings.Instance.BlobSasUri;
                if (!string.IsNullOrWhiteSpace(blobSasUri))
                {
                    return blobSasUri;
                }
                else
                {
                    return Settings.Instance.AccountSasUri;
                }
            }
        }
    }

    public class MonitoringSession
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RuleType RuleType { get; set; } = RuleType.Diagnostics;

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

        //
        // Added for RuleType.AlwaysOnRule
        //

        public int IntervalDays { get; set; }
        public int ActionsInInterval { get; set; }
        public TimeSpan ProcessWarmupTime { get; set; } = TimeSpan.FromHours(1);

        public string DefaultHostName { get; set; }
        public string BlobStorageHostName { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public AnalysisStatus AnalysisStatus { get; set; }

        internal void SaveToDisk(string cpuMonitoringActive)
        {
            this.ToJsonFile(cpuMonitoringActive);
        }

        public List<MonitoringFile> FilesCollected { get; set; }
    }
    

    public class ActiveMonitoringSession
    {
        public MonitoringSessionResponse Session { get; set; }
        public List<MonitoringLogsPerInstance> MonitoringLogs { get; set; }
    }
}
