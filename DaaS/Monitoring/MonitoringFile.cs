using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace DaaS
{
    public class MonitoringFile
    {
        [JsonConstructor]
        public MonitoringFile(string fileName, string relativePath)
        {
            FileName = fileName;
            RelativePath = relativePath;
        }
        [JsonProperty]
        public string FileName { get; }

        [JsonProperty]
        public string RelativePath { get; }

        [JsonProperty]
        public string ReportFile { get; set; }

        [JsonProperty]
        public string ReportFileRelativePath { get; set; }

        [JsonProperty]
        public List<string> AnalysisErrors { get; set; }

        public static string GetRelativePath(string sessionId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }
            var logsFolderPath = MonitoringSessionController.GetCpuMonitoringPath(MonitoringSessionDirectories.Logs, true);
            string path = Path.Combine(logsFolderPath, sessionId, fileName);
            return path.ConvertBackSlashesToForwardSlashes();
        }
    }
}
