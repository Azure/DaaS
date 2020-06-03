using System;
using System.Collections.Generic;

namespace ClrProfilingAnalyzer
{
    class ClrProfilingAnalyzerStats
    {
        public string StatsType;
        public string InstanceName;
        public string SiteName;
        public int ProcessId;
        public string TraceFileName;
        public Guid ActivityId;
        public double TimeToOpenTraceFileInSeconds;
        public double TimeToParseIISEventsInSeconds;
        public double TimeToParseNetCoreEventsInSeconds;
        public double TimeToLoadTraceInfoInSeconds;
        public double TimeToLoadSymbolsInSeconds;
        public double TimeToGenerateStackTraces;
        public double TimeToGenerateStackTracesAspNetCore;
        public double TimeToGenerateCpuStacks;
        public double TimeToGenerateParseClrExceptions;
        public double TimeToDumpFailedCoreRequests;
        public int StackTraceCount;
        public int StackTraceCountAsync;
        public int SlowRequestCount;
        public int SlowRequestCountAspNetCore;
        public int FailedRequestCount;
        public int FailedRequestCountAspNetCore;
        public int FailedRequestsWithClrExceptions;
        public int OutProcClrExceptions;
        public int TotalRequestCount;
        public int IncompleteRequestCount;
        public int FiftyPercentile;
        public int NinetyPercentile;
        public int NinetyFifthPercentile;
        public double AverageResponseTime;
        public List<ModuleInfo> ModuleExecutionPercent;
        public float CPUTimeByThisProcess;
        public double PercentCPUMachine;
        public double PercentCPUProcess;
        public double TraceDuration;
        public List<IisPipelineEvent> SlowestPipelineEvents;

    }
}
