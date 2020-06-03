using System;

namespace DaaS
{
    public class MonitoredProcess
    {
        public TimeSpan CPUTimeStart;
        public TimeSpan CPUTimeCurrent;
        public DateTime LastMonitorTime;
        public int ThresholdExeededCount;
        public string Name;
    }
}
