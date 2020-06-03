using System;

namespace ClrProflingCollector
{
    class ClrProfilingCollectorStats
    {
        public string StatsType;
        public string InstanceName;
        public string SiteName;
        public int ProcessId;
        public double TraceDurationInSeconds;
        public double TimeToStartTraceInSeconds;
        public double TimeToStopTraceInSeconds;
        public double TimeToGenerateRawStackTraces;
        public string TraceFileName;
        public long TraceFileSizeInMb;
        public Guid ActivityId;
        public string DotNetCoreProcess;
    }
}
