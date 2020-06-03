using Newtonsoft.Json;
using System;

namespace ClrProfilingAnalyzer
{
    public static class SystemExtension
    {
        public static T Clone<T>(this T source)
        {
            var serialized = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }

    public class AspNetCoreRequest
    {
        public string RequestId;
        public string Path;
        public string ShortActivityId;
        public Guid ActivityId;
        public string RelatedActivityId;
        public double EndTimeRelativeMSec = 0;
        public double StartTimeRelativeMSec = 0;
        public int StatusCode;
        public int ProcessId;
        public bool HasActivityStack;
        public bool IncompleteRequest = false;
    }

    public enum AspNetCoreRequestEventType
    {
        Start,
        Stop,
        Message
    }

    public class AspNetCoreRequestId
    {
        public string ShortActivityId;
        public Guid ActivityId;
    }
}
