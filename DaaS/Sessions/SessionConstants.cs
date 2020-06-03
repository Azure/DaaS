namespace DaaS.Sessions
{
    class SessionConstants
    {
        public const string SessionFileNameFormat = "yyMMdd_HHmmssffff";
        public const string SessionFileExtension = ".xml";
    }

    class SessionXml
    {
        public const string Session = "Session";
        public const string SiteName = "SiteName";
        public const string TimeRange = "TimeRange";
        public const string StartTime = "StartTime";
        public const string EndTime = "EndTime";
        public const string TimeFormat = "O";
        public const string Description = "Description";
        public const string BlobSasUri = "BlobSasUri"; 
        public const string Instances = "Instances";
        public const string InstancesCollected = "InstancesCollected";
        public const string Instance = "Instance";
        public const string Diagnosers = "Diagnosers";
        public const string Diagnoser = "Diagnoser";
        public const string Collector = "Collector";
        public const string Analyzer = "Analyzer";
        public const string Status = "Status";
        public const string Report = "Report";
        public const string Name = "Name";
        public const string Path = "Path";
        public const string FileSize = "FileSize";
        public const string SessionId = "SessionId";
        public const string Logs = "Logs";
        public const string Log = "Log";
        public const string FailureCount = "FailureCount";
        public const string RunCount = "RunCount";
        public const string Error = "Error";
        public const string AnalysisStarted = "AnalysisStarted";
        public const string AnalyzerStartTime = "AnalyzerStartTime";
        public const string InstanceAnalyzing = "InstanceAnalyzing";
    }

    class SessionDirectories
    {
        public static readonly string SessionsDir = "Sessions";
        public static readonly string ActiveSessionsDir = SessionsDir + @"\" + SessionStatus.Active;
        public static readonly string CollectedLogsOnlySessionsDir = SessionsDir + @"\" + SessionStatus.CollectedLogsOnly;
        public static readonly string CompletedSessionsDir = SessionsDir + @"\" + SessionStatus.Complete;        
    }
}
