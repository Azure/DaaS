using System;

namespace DiagnosticsExtension
{
    public class FrebFile
    {
        public string FileName { get; set; }
        public string URL { get; set; }
        public string Verb { get; set; }
        public string AppPoolName { get; set; }
        public int StatusCode { get; set; }
        public int TimeTaken { get; set; }
        public string SiteId { get; set; }
        public string Href { get; set; }
        public DateTime DateCreated { get; set; }
    }
}