namespace ClrProfilingAnalyzer
{
    class IisPrebeginModuleEvent : IisPipelineEvent
    {
        public override string ToString()
        {
            return Name + " (PreBegin)";
        }
    }
}
