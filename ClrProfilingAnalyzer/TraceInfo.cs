//-----------------------------------------------------------------------
// <copyright file="TraceInfo.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

namespace ClrProfilingAnalyzer
{
    class ModuleInfo
    {
        public string ModuleName;
        public double Percent;
        public double TimeSpent;
    }
    internal class TraceInfo
    {
        public double TraceDuration;
        public float TotalRequests;
        public float CPUTimeByThisProcess;
        public double CPUMetricPerInterval;
        public double CPUTimeTotalMetrics;
        public int ProcessId;
        public double NumberOfProcessors;
        public int SuccessfulRequests;
        public int FailedRequests;
        public int IncompleteRequests;
        public double AverageResponseTime;
        public int FiftyPercentile;
        public int NinetyPercentile;
        public int NinetyFifthPercentile;
        public double TotalTimeInRequestExecution;
        public string InstanceName;
        public string TraceFileLocation;       
        public string TraceStartTime;
        public string TraceFileName;
        public bool ContainsIisEvents;
        public System.Collections.Generic.List<ModuleInfo> ModuleExecutionPercent { get; set; }        
    }
}
