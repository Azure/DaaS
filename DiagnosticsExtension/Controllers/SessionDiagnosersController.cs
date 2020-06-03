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

            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            List<DiagnoserSessionMinDetails> retVal = new List<DiagnoserSessionMinDetails>();

            foreach(DiagnoserSession diagSession in session.GetDiagnoserSessions())
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

            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            List<DiagnoserSessionDetails> retVal = new List<DiagnoserSessionDetails>();

            foreach (DiagnoserSession diagSession in session.GetDiagnoserSessions())
            {
                DiagnoserSessionDetails tempSession = new DiagnoserSessionDetails
                {
                    Name = diagSession.Diagnoser.Name,
                    CollectorStatus = diagSession.CollectorStatus,
                    AnalyzerStatus = diagSession.AnalyzerStatus,
                    CollectorStatusMessages = diagSession.CollectorStatusMessages,
                    AnalyzerStatusMessages = diagSession.AnalyzerStatusMessages
                };

                foreach (String analyzerError in diagSession.GetAnalyzerErrors())
                {
                    tempSession.AddAnalyzerError(analyzerError);
                }

                foreach (String collectorError in diagSession.GetCollectorErrors())
                {
                    tempSession.AddCollectorError(collectorError);
                }

                int minLogRelativePathSegments = Int32.MaxValue;
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

                    int relativePathSegments = temp.RelativePath.Split('\\').Length;

                    if (relativePathSegments == minLogRelativePathSegments)
                    {
                        temp.RelativePath = temp.RelativePath.Replace('\\', '/');
                        tempSession.AddLog(temp);
                    }
                    else if (relativePathSegments < minLogRelativePathSegments)
                    {
                        minLogRelativePathSegments = relativePathSegments;
                        temp.RelativePath = temp.RelativePath.Replace('\\', '/');
                        tempSession.ClearReports();
                        tempSession.AddLog(temp);
                    }
                }

                int minReportRelativePathSegments = Int32.MaxValue;
                foreach (Report report in diagSession.GetReports())
                {
                    ReportDetails temp = new ReportDetails
                    {
                        FileName = report.FileName,
                        RelativePath = report.RelativePath,
                        FullPermanentStoragePath = report.FullPermanentStoragePath
                    };

                    int relativePathSegments = temp.RelativePath.Split('\\').Length;

                    if (relativePathSegments == minReportRelativePathSegments)
                    {
                        temp.RelativePath = temp.RelativePath.Replace('\\', '/');
                        tempSession.AddReport(temp);
                    }
                    else if (relativePathSegments < minReportRelativePathSegments)
                    {
                        minReportRelativePathSegments = relativePathSegments;
                        temp.RelativePath = temp.RelativePath.Replace('\\', '/');
                        tempSession.ClearReports();
                        tempSession.AddReport(temp);
                    }
                }

                retVal.Add(tempSession);
            }

            return retVal;
        }
    }
}
