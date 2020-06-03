using System;

namespace DiagnosticsExtension
{
    public class ServerSideEvent
    {
        public DateTime DateAndTime { get; set; }
        public string Source { get; set; }
        public string EventID { get; set; }
        public string TaskCategory { get; set; }
        public string Description { get; set; }
        public string Level { get; set; }
        public string EventRecordID { get; set; }
        public string Computer { get; set; }
    }
}