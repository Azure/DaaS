using DiagnosticsExtension.Models;
using DaaS;
using DaaS.Diagnostics;
using DaaS.Sessions;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class SessionDiagnosersController : ApiController
    {
        public List<DiagnoserSessionMinDetails> Get(string sessionId)
        {
            SessionController sessionController = new SessionController();

            ISession session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

            List<DiagnoserSessionMinDetails> retVal = new List<DiagnoserSessionMinDetails>();

            foreach(IDiagnoserSession diagSession in session.GetDiagnoserSessions())
            {
                DiagnoserSessionMinDetails temp = new DiagnoserSessionMinDetails
                {
                    Name = diagSession.Diagnoser.Name,
                    CollectorStatus = diagSession.CollectorStatus,
                    AnalyzerStatus = diagSession.AnalyzerStatus,
                    Logs = new List<String>(diagSession.GetLogs().Select(p => p.FileName)),
                    Reports = new List<String>(diagSession.GetReports().Select(p => p.FileName))
                };

                retVal.Add(temp);
            }

            return retVal;
        }

        public List<DiagnoserSessionDetails> Get(string sessionId, bool detailed)
        {
            SessionController sessionController = new SessionController();

            ISession session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

            List<DiagnoserSessionDetails> retVal = new List<DiagnoserSessionDetails>();

            foreach (IDiagnoserSession diagSession in session.GetDiagnoserSessions())
            {
                DiagnoserSessionDetails tempSession = new DiagnoserSessionDetails
                {
                    Name = diagSession.Diagnoser.Name,
                    CollectorStatus = diagSession.CollectorStatus,
                    AnalyzerStatus = diagSession.AnalyzerStatus
                };
                foreach (Log log in diagSession.GetLogs())
                {
                    LogDetails temp = new LogDetails
                    {
                        FileName = log.FileName,
                        RelativePath = log.RelativePath,
                        FullPermanentStoragePath = log.FullPermanentStoragePath,
                        StartTime = log.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        EndTime = log.EndTime.ToString("yyyy-MM-dd HH:mm:ss")
                    };
                    tempSession.AddLog(temp);
                }
                foreach (Report report in diagSession.GetReports())
                {
                    ReportDetails temp = new ReportDetails
                    {
                        FileName = report.FileName,
                        RelativePath = report.RelativePath,
                        FullPermanentStoragePath = report.FullPermanentStoragePath
                    };
                    tempSession.AddReport(temp);
                }

                retVal.Add(tempSession);
            }

            return retVal;
        }
    }
}
