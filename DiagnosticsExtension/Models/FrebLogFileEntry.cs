using System;

namespace DiagnosticsExtension
{
    class FrebLogFileEntry
    {
        public string Name { get; set; }
        public int Size { get; set; }
        public DateTime Mtime { get; set; }
        public string Mime { get; set; }
        public string Href { get; set; }
        public string Path { get; set; }
    }
}