using DaaS;
using DaaS.Diagnostics;
using DaaS.Sessions;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Web;

namespace DiagnosticsExtension.Models
{
    public class DiagnoserList
    {
        [DataMember]
        public List<string> Diagnosers;

        public DiagnoserList(IEnumerable<string> diagnosers)
        {
            Diagnosers = new List<string>(diagnosers);
        }
    }


    public class SessionDetailsList
    {
        [DataMember]
        public List<SessionDetails> Sessions = new List<SessionDetails>();

        public void AddSession(SessionDetails sessionInfo)
        {
            Sessions.Add(sessionInfo);
        }
    }

    public class SessionMinDetails
    {
        [DataMember]
        public string Description;
        [DataMember]
        public string SessionId;
        [DataMember]
        public string StartTime;
        [DataMember]
        public string EndTime;
        [DataMember]
        public SessionStatus Status;
        [DataMember]
        public List<String> DiagnoserSessions = new List<String>();

        public void AddDiagnoser(String diagnoserName)
        {
            DiagnoserSessions.Add(diagnoserName);
        }
    }

    public class SessionDetails
    {
        [DataMember]
        public string Description;
        [DataMember]
        public string SessionId;
        [DataMember]
        public string StartTime;
        [DataMember]
        public string EndTime;
        [DataMember]
        public SessionStatus Status;
        [DataMember]
        public List<DiagnoserSessionDetails> DiagnoserSessions = new List<DiagnoserSessionDetails>();

        public void AddDiagnoser(DiagnoserSessionDetails diagnoserSessionDetails)
        {
            DiagnoserSessions.Add(diagnoserSessionDetails);
        }
    }

    public class DiagnoserSessionMinDetails
    {
        [DataMember(Name = "Name")]
        public string Name;
        [DataMember(Name = "CollectorStatus")]
        public DiagnosisStatus CollectorStatus;
        [DataMember(Name = "AnalyzerStatus")]
        public DiagnosisStatus AnalyzerStatus;
        [DataMember(Name = "Logs")]
        public List<String> Logs = new List<String>();
        [DataMember(Name = "Reports")]
        public List<String> Reports = new List<String>();
        public void AddLog(String logFileName)
        {
            Logs.Add(logFileName);
        }
        public void AddReport(String reportFileName)
        {
            Reports.Add(reportFileName);
        }
    }


    public class DiagnoserSessionDetails
    {
        [DataMember(Name = "Name")]
        public string Name;
        [DataMember(Name = "CollectorStatus")]
        public DiagnosisStatus CollectorStatus;
        [DataMember(Name = "AnalyzerStatus")]
        public DiagnosisStatus AnalyzerStatus;
        [DataMember(Name = "Logs")]
        public List<LogDetails> Logs = new List<LogDetails>();
        [DataMember(Name = "Reports")]
        public List<ReportDetails> Reports = new List<ReportDetails>();
        public void AddLog(LogDetails logDetails)
        {
            Logs.Add(logDetails);
        }
        public void AddReport(ReportDetails reportDetails)
        {
            Reports.Add(reportDetails);
        }
    }

    public class LogDetails
    {
        [DataMember(Name = "Filename")]
        public string FileName;
        [DataMember(Name = "RelativePath")]
        public string RelativePath;
        [DataMember(Name = "FullPermanentStoragePath")]
        public string FullPermanentStoragePath;
        [DataMember(Name = "StartTime")]
        public string StartTime;
        [DataMember(Name = "EndTime")]
        public string EndTime;
    }

    public class ReportDetails
    {
        [DataMember(Name = "Filename")]
        public string FileName;
        [DataMember(Name = "RelativePath")]
        public string RelativePath;
        [DataMember(Name = "FullPermanentStoragePath")]
        public string FullPermanentStoragePath;
    }


    public class NewSessionInfo
    {
        [DataMember]
        public bool RunLive;
        [DataMember]
        public bool CollectLogsOnly;
        [DataMember]
        public string StartTime;
        [DataMember]
        public string EndTime;
        [DataMember]
        public string TimeSpan;
        [DataMember]
        public List<string> Diagnosers;
        [DataMember]
        public List<string> Instances;
        [DataMember]
        public string Description;
    }

    public class Settings
    {
        [DataMember]
        public List<String> Diagnosers;
        [DataMember]
        public string TimeSpan;
        [DataMember]
        public string BlobSasUri;
        [DataMember]
        public string BlobContainer;
        [DataMember]
        public string BlobKey;
    }
}