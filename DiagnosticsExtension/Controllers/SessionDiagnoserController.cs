using DiagnosticsExtension.Models;
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
    public class SessionDiagnoserController : ApiController
    {
        public DiagnoserSessionMinDetails Get(string sessionId, string diagnoser)
        {
            SessionController sessionController = new SessionController();

            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            DiagnoserSession diagSession = session.GetDiagnoserSessions().First(p => p.Diagnoser.Name.Equals(diagnoser, StringComparison.OrdinalIgnoreCase));

            DiagnoserSessionMinDetails retVal = new DiagnoserSessionMinDetails
            {
                Name = diagSession.Diagnoser.Name,
                CollectorStatus = diagSession.CollectorStatus,
                AnalyzerStatus = diagSession.AnalyzerStatus,
                Logs = new List<String>(diagSession.GetLogs().Select(p => p.FileName)),
                Reports = new List<String>(diagSession.GetReports().Select(p => p.FileName))
            };

            return retVal;
        }

        public DiagnoserSessionDetails Get(string sessionId, string diagnoser, bool detailed)
        {
            SessionController sessionController = new SessionController();

            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            DiagnoserSession diagSession = session.GetDiagnoserSessions().First(p => p.Diagnoser.Name.Equals(diagnoser, StringComparison.OrdinalIgnoreCase));

            DiagnoserSessionDetails retVal = new DiagnoserSessionDetails
            {
                Name = diagSession.Diagnoser.Name,
                CollectorStatus = diagSession.CollectorStatus,
                AnalyzerStatus = diagSession.AnalyzerStatus,
                CollectorStatusMessages = diagSession.CollectorStatusMessages,
                AnalyzerStatusMessages = diagSession.AnalyzerStatusMessages
            };

            foreach(String analyzerError in diagSession.GetAnalyzerErrors())
            {
                retVal.AddAnalyzerError(analyzerError);
            }

            foreach (String collectorError in diagSession.GetCollectorErrors())
            {
                retVal.AddCollectorError(collectorError);
            }

            int minLogRelativePathSegments = Int32.MaxValue;
            foreach(Log log in diagSession.GetLogs())
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
                    retVal.AddLog(temp);
                }
                else if (relativePathSegments < minLogRelativePathSegments)
                {
                    minLogRelativePathSegments = relativePathSegments;
                    temp.RelativePath = temp.RelativePath.Replace('\\', '/');
                    retVal.ClearReports();
                    retVal.AddLog(temp);
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
                    retVal.AddReport(temp);
                }
                else if (relativePathSegments < minReportRelativePathSegments)
                {
                    minReportRelativePathSegments = relativePathSegments;
                    temp.RelativePath = temp.RelativePath.Replace('\\', '/');
                    retVal.ClearReports();
                    retVal.AddReport(temp);
                }
            }

            return retVal;
        }
    }
}
