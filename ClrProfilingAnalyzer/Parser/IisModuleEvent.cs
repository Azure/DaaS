using Microsoft.Diagnostics.Tracing.Parsers;

namespace ClrProfilingAnalyzer
{
    class IisModuleEvent : IisPipelineEvent
    {
        public RequestNotification Notification;
        public bool fIsPostNotification;
        public bool foundEndEvent = false;

        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, Notification.ToString());
        }
    }
}
