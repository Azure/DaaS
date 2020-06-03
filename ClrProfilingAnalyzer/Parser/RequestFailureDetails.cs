using ClrProfilingAnalyzer.Parser;
using Microsoft.Diagnostics.Tracing.Parsers;
using System.Collections.Generic;

namespace ClrProfilingAnalyzer
{
    public class RequestFailureDetails
    {
        public string ModuleName;
        public RequestNotification Notification;
        public string HttpReason;
        public int HttpStatus;
        public int HttpSubStatus;
        public int ErrorCode;
        public string ConfigExceptionInfo;
        public double TimeStampRelativeMSec;
        public List<ExceptionDetails> ExceptionDetails;
    }
}
