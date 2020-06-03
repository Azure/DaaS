using System;

namespace DiagnosticsExtension.Parsers
{
    public class LogFile
    {
        public string Content { get; set; }
        public DateTime CreationTimeUtc { get; set; }
        public string FileName { get; set; }
    }
}
