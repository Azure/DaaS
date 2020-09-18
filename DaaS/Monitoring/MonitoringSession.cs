//-----------------------------------------------------------------------
// <copyright file="MonitoringSession.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using DaaS.Storage;
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

    public class MonitoringSessionResponse : MonitoringSession
    {
        private readonly string _blobSasUri = string.Empty;

        public MonitoringSessionResponse(MonitoringSession s)
        {
            Mode = s.Mode;
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
            _blobSasUri = s.BlobSasUri;
    }
        public new string BlobSasUri
        {
            get
            {
                return BlobController.GetActualBlobSasUri(_blobSasUri);
            }
        }
    }

    public class MonitoringSession
    {
        private string _blobSasUri = string.Empty;

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
        public string BlobStorageHostName { get; set; }
        public string BlobSasUri
        {
            get
            {
                if (string.IsNullOrEmpty(_blobSasUri) && Configuration.Settings.Instance.IsBlobSasUriConfiguredAsEnvironmentVariable())
                {
                    return Configuration.Settings.WebSiteDaasStorageSasUri;
                }
                else
                {
                    return _blobSasUri;
                }
            }
            set
            {
                _blobSasUri = value;
            }
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public AnalysisStatus AnalysisStatus { get; set; }
        public List<MonitoringFile> FilesCollected { get; set; }
    }
    

    public class ActiveMonitoringSession
    {
        public MonitoringSessionResponse Session { get; set; }
        public List<MonitoringLogsPerInstance> MonitoringLogs { get; set; }
    }
}
