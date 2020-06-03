using System;

namespace DaaS
{
    class AnalysisRequest
    {
        public DateTime StartTime { get; set; }
        public string LogFileName { get; set; }
        public DateTime ExpirationTime { get; set; }
        public string SessionId { get; set; }
        public int RetryCount { get; set; }
        public string BlobSasUri { get; set; }
    }
}
