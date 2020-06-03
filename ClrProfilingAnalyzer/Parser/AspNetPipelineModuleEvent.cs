using System;

namespace ClrProfilingAnalyzer.Parser
{
    class AspNetPipelineModuleEvent : IisPipelineEvent
    {
        public string ModuleName;
        public bool foundEndEvent = false;

        public override string ToString()
        {
            return ModuleName;
        }
    }
}
