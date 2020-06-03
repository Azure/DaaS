using System;
using System.Collections.Generic;

namespace ClrProfilingAnalyzer
{
    public class IisRequest
    {
        public string Method;
        public string Path;
        public Guid ContextId;
        public int BytesSent;
        public int BytesReceived;
        public int StatusCode;
        public int SubStatusCode;
        public RequestFailureDetails FailureDetails;
        public double EndTimeRelativeMSec;
        public double StartTimeRelativeMSec;
        public List<IisPipelineEvent> PipelineEvents = new List<IisPipelineEvent>();
        public Guid RelatedActivityId;
        public bool HasActivityStack;
        public bool HasThreadStack;
    }
}
