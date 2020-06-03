namespace ClrProfilingAnalyzer
{
    public class IisPipelineEvent
    {
        public string Name;
        public int ProcessId;
        public int StartThreadId;
        public int EndThreadId;
        public double StartTimeRelativeMSec = 0;
        public double EndTimeRelativeMSec = 0;
        public int ChildRequestRecurseLevel = 0;
        public override string ToString()
        {
            return Name;
        }
    }
}
