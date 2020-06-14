//-----------------------------------------------------------------------
// <copyright file="Sessions.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using SiteDiagnostics;
using SiteDiagnostics.Diagnostics;
using SiteDiagnostics.Storage;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;


namespace MySiteDiagnostics.Sessions
{
    public enum DiagnosisStatus
    {
        NotRequested,
        WaitingForInputs,
        InProgress,
        Complete,
    }

    public enum SessionStatus
    {
        CollectingLogs,
        AnalyzingLogs,
        Complete
    }

    public class SessionId
    {
        public string Id { get; internal set; }

        public SessionId(string id)
        {
            Id = id;
        }

        public override string ToString()
        {
            return Id;
        }
    }


    public interface ISessionController
    {
        string BlobStorageSasUri { get; set; }

        ISession CollectLogs(List<IDiagnoser> diagnosers, DateTime utcStartTime, DateTime utcEndTime, string description = null);
        ISession CollectLiveDataLogs(List<IDiagnoser> diagnosers, TimeSpan timeSpan, string description = null);
        ISession Analyze(ISession session, List<IDiagnoser> diagnosers, string description = null);
        ISession Analyze(ISession session, string description = null);
        ISession Troubleshoot(List<IDiagnoser> diagnosers, DateTime utcStartTime, DateTime utcEndTime, string description = null);
        ISession TroubleshootLiveData(List<IDiagnoser> diagnosers, TimeSpan timeSpan, string description = null);

        IEnumerable<ISession> GetAllSessions();
        IEnumerable<ISession> GetAllCompletedSessions(); //andimarc
        IEnumerable<ISession> GetAllSessionsThatNeedAnalysis();
        IEnumerable<ISession> GetAllPendingSessions();
        IEnumerable<ISession> GetSessionWithId(SessionId sessionId);

        IEnumerable<IDiagnoser> GetAllDiagnosers();

        void RunPendingSessions();
    }

    public interface ISession : IFile
    {
        string SiteName { get; }
        DateTime StartTime { get; }
        DateTime EndTime { get; }
        SessionStatus Status { get; }
        SessionId SessionId { get; }
        string Description { get; }

        IEnumerable<IDiagnoserSession> GetDiagnosers();
    }

    public interface IDiagnoserSession
    {
        IDiagnoser Diagnoser { get; }

        DiagnosisStatus CollectorDiagnosisStatus { get; set; }
        DiagnosisStatus AnalyzerDiagnosisStatus { get; set; }

        bool HasThisInstanceRunTheCollector();
        bool HaveAllInstancesRunTheCollector();

        IEnumerable<ILog> GetLogs();
        IEnumerable<IReport> GetReports();

        void AddLog(ILog log);
        void AddReport(IReport report);
    }







    class SessionControllerStub : ISessionController
    {
        private Hashtable Sessions = new Hashtable();
        private Hashtable CompletedSessions = new Hashtable();
        private Hashtable PendingSessions = new Hashtable();
        private Hashtable SessionsThatNeedAnalysis = new Hashtable();

        private Hashtable CreatedSessions = new Hashtable();

        private DiagnoserSessionStub CreateDiagnoserSession(string type, int numFiles, DiagnosisStatus collectStatus, DiagnosisStatus analyzeStatus)
        {
            DiagnoserSessionStub d = new DiagnoserSessionStub
            {
                Diagnoser = new DiagnoserStub(type + "LogsDiag"),
                CollectorDiagnosisStatus = collectStatus,
                AnalyzerDiagnosisStatus = analyzeStatus
            };

            for (int i = 0; i < numFiles; i++)
            {
                d.AddLog(new LogStub
                {
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now,
                    FileName = type + "Log" + i + ".txt",
                    FullPath = @"\\path\to\" + type + "Log" + i + ".txt"
                });
                d.AddReport(new ReportStub
                {
                    FileName = "httpReport" + i + ".txt",
                    FullPath = @"\\path\to\" + type + "Report" + i + ".txt"
                });
            }

            return d;
        }

        public SessionControllerStub()
        {
            SessionStub s0 = new SessionStub
            {
                SiteName = "demoSite",
                StartTime = DateTime.Now.AddMinutes(5),
                EndTime = DateTime.Now.AddMinutes(10),
                Status = SessionStatus.Complete,
                SessionId = new SessionId("SESSION_0"),
                Description = "session0"
            };
            s0.AddDiagnoser(CreateDiagnoserSession("Http", 3, DiagnosisStatus.Complete, DiagnosisStatus.Complete));
            s0.AddDiagnoser(CreateDiagnoserSession("CrashDump", 4, DiagnosisStatus.Complete, DiagnosisStatus.Complete));
            s0.AddDiagnoser(CreateDiagnoserSession("ProcDump", 2, DiagnosisStatus.Complete, DiagnosisStatus.Complete));

            SessionStub s1 = new SessionStub
            {
                SiteName = "demoSite",
                StartTime = DateTime.Now.AddMinutes(5),
                EndTime = DateTime.Now.AddMinutes(10),
                Status = SessionStatus.AnalyzingLogs,
                SessionId = new SessionId("SESSION_1"),
                Description = "session1"
            };
            s1.AddDiagnoser(CreateDiagnoserSession("Http", 3, DiagnosisStatus.Complete, DiagnosisStatus.InProgress));
            s1.AddDiagnoser(CreateDiagnoserSession("ProcDump", 2, DiagnosisStatus.Complete, DiagnosisStatus.InProgress));

            SessionStub s2 = new SessionStub
            {
                SiteName = "demoSite",
                StartTime = DateTime.Now.AddMinutes(5),
                EndTime = DateTime.Now.AddMinutes(10),
                Status = SessionStatus.CollectingLogs,
                SessionId = new SessionId("SESSION_2"),
                Description = "session2"
            };
            s2.AddDiagnoser(CreateDiagnoserSession("ProcDump", 2, DiagnosisStatus.InProgress, DiagnosisStatus.WaitingForInputs));

            SessionStub s3 = new SessionStub
            {
                SiteName = "demoSite",
                StartTime = DateTime.Now.AddMinutes(5),
                EndTime = DateTime.Now.AddMinutes(10),
                Status = SessionStatus.Complete,
                SessionId = new SessionId("SESSION_3"),
                Description = "session3"
            };
            s3.AddDiagnoser(CreateDiagnoserSession("Http", 5, DiagnosisStatus.Complete, DiagnosisStatus.NotRequested));

            Sessions.Add(s0.SessionId.Id, s0);
            CompletedSessions.Add(s0.SessionId.Id, s0);

            Sessions.Add(s1.SessionId.Id, s1);
            PendingSessions.Add(s1.SessionId.Id, s1);
            Sessions.Add(s2.SessionId.Id, s2);
            PendingSessions.Add(s2.SessionId.Id, s2);

            Sessions.Add(s3.SessionId.Id, s3);
            SessionsThatNeedAnalysis.Add(s3.SessionId.Id, s3);
        }

        public string BlobStorageSasUri { get; set; }

        public ISession CollectLogs(List<IDiagnoser> diagnosers, DateTime utcStartTime, DateTime utcEndTime, string description = null)
        {
            return (SessionStub)CreatedSessions["collectScheduled"];
        }
        public ISession CollectLiveDataLogs(List<IDiagnoser> diagnosers, TimeSpan timeSpan, string description = null)
        {
            return (SessionStub)CreatedSessions["collectLive"];
        }
        public ISession Analyze(ISession session, List<IDiagnoser> diagnosers, string description = null)
        {
            return (SessionStub)CreatedSessions["analyze0"];
        }
        public ISession Analyze(ISession session, string description = null)
        {
            return (SessionStub)CreatedSessions["analyze1"];
        }
        public ISession Troubleshoot(List<IDiagnoser> diagnosers, DateTime utcStartTime, DateTime utcEndTime, string description = null)
        {
            return (SessionStub)CreatedSessions["troubleshootScheduled"];
        }
        public ISession TroubleshootLiveData(List<IDiagnoser> diagnosers, TimeSpan timeSpan, string description = null)
        {
            return (SessionStub)CreatedSessions["troubleshootLive"];
        }


        public IEnumerable<ISession> GetAllSessions()
        {
            ICollection keys = Sessions.Keys;
            List<ISession> retVal = new List<ISession>();
            foreach(Object key in keys)
            {
                retVal.Add((ISession)Sessions[key]);
            }
            return retVal;
        }

        public IEnumerable<ISession> GetAllCompletedSessions()
        {
            ICollection keys = CompletedSessions.Keys;
            List<ISession> retVal = new List<ISession>();
            foreach (Object key in keys)
            {
                retVal.Add((ISession)CompletedSessions[key]);
            }
            return retVal;
        }

        public IEnumerable<ISession> GetAllSessionsThatNeedAnalysis()
        {
            ICollection keys = SessionsThatNeedAnalysis.Keys;
            List<ISession> retVal = new List<ISession>();
            foreach (Object key in keys)
            {
                retVal.Add((ISession)SessionsThatNeedAnalysis[key]);
            }
            return retVal;
        }
        public IEnumerable<ISession> GetAllPendingSessions()
        {
            ICollection keys = PendingSessions.Keys;
            List<ISession> retVal = new List<ISession>();
            foreach (Object key in keys)
            {
                retVal.Add((ISession)PendingSessions[key]);
            }
            return retVal;
        }
        public IEnumerable<ISession> GetSessionWithId(SessionId sessionId)
        {
            List<ISession> retval = new List<ISession>();
            retval.Add((ISession)Sessions[sessionId.Id]);
            return retval;
        }

        public IEnumerable<IDiagnoser> GetAllDiagnosers()
        {
            List<DiagnoserStub> diagnosers = new List<DiagnoserStub>();
            diagnosers.Add(new DiagnoserStub("HttpLogsDiagnoser"));
            diagnosers.Add(new DiagnoserStub("ProcDumpDiagnoser"));
            diagnosers.Add(new DiagnoserStub("CrashDumpDiagnoser"));
            return diagnosers;
        }


        public void RunPendingSessions()
        {
        }
    }

    class SessionStub : ISession
    {
        private List<DiagnoserSessionStub> _diagnosers = new List<DiagnoserSessionStub>();

        public void AddDiagnoser(DiagnoserSessionStub diagnoser)
        {
            _diagnosers.Add(diagnoser);
        }

        public string SiteName
        {
            get;
            internal set;
        }
        public DateTime StartTime
        {
            get;
            internal set;
        }
        public DateTime EndTime
        {
            get;
            internal set;
        }
        public SessionStatus Status
        {
            get;
            internal set;
        }
        public SessionId SessionId
        {
            get;
            internal set;
        }
        public string Description
        {
            get;
            internal set;
        }

        public IEnumerable<IDiagnoserSession> GetDiagnosers()
        {
            return _diagnosers;
        }

        public string FileName
        {
            get
            {
                return "session.txt";
            }
        }

        public string FullPath
        {
            get
            {
                return "\\path\\to\\session.txt";
            }
        }
    }

    class DiagnoserSessionStub : IDiagnoserSession
    {
        private List<ILog> _logs = new List<ILog>();
        private List<IReport> _reports = new List<IReport>();
        public IDiagnoser Diagnoser
        {
            get;
            internal set;
        }

        public DiagnosisStatus CollectorDiagnosisStatus { get; set; }
        public DiagnosisStatus AnalyzerDiagnosisStatus { get; set; }

        public bool HasThisInstanceRunTheCollector()
        {
            return true;
        }
        public bool HaveAllInstancesRunTheCollector()
        {
            return true;
        }

        public IEnumerable<ILog> GetLogs()
        {
            return _logs;
        }

        public IEnumerable<IReport> GetReports()
        {
            return _reports;
        }

        public void AddLog(ILog log)
        {
            _logs.Add(log);
        }
        public void AddReport(IReport report)
        {
            _reports.Add(report);
        }
    }




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

    public class SessionDetails
    {
        [DataMember]
        public string Name;
        [DataMember]
        public string Id;
        [DataMember]
        public DateTime StartTime;
        [DataMember]
        public DateTime EndTime;
        [DataMember]
        public SessionStatus Status;
        [DataMember]
        public List<DiagnoserSessionDetails> Diagnosers = new List<DiagnoserSessionDetails>();

        public void AddDiagnoser(DiagnoserSessionDetails diagnoserSessionDetails)
        {
            Diagnosers.Add(diagnoserSessionDetails);
        }
    }


    public class DiagnoserSessionDetails
    {
        [DataMember(Name = "name")]
        public string Name;
        [DataMember(Name = "collectstatus")]
        public DiagnosisStatus CollectStatus;
        [DataMember(Name = "analyzestatus")]
        public DiagnosisStatus AnalyzeStatus;
        [DataMember(Name = "logs")]
        public List<LogDetails> Logs = new List<LogDetails>();
        [DataMember(Name = "reports")]
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
        [DataMember(Name = "starttime")]
        public DateTime StartTime;
        [DataMember(Name = "endtime")]
        public DateTime EndTime;
        [DataMember(Name = "filename")]
        public string FileName;
        [DataMember(Name = "filepath")]
        public string FilePath;
    }

    public class ReportDetails
    {
        [DataMember(Name = "filename")]
        public string FileName;
        [DataMember(Name = "filepath")]
        public string FilePath;
    }
}
