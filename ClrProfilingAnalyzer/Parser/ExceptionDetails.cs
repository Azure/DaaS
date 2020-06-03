using System.Collections.Generic;

namespace ClrProfilingAnalyzer.Parser
{
    public class ExceptionDetails
    {
        public string ExceptionType;
        public string ExceptionMessage;
        public int ThreadId;
        public int ProcessId;
        public string ProcessName;
        public double TimeStampRelativeMSec;
        public List<string> StackTrace;
        public int StackTraceHash;
    }
}