//-----------------------------------------------------------------------
// <copyright file="SessionsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

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
    public class SessionsController : ApiController
    {
        public List<SessionMinDetails> Get(string type)
        {
            SessionController sessionController = new SessionController();

            IEnumerable<ISession> sessions;

            switch (type)
            {
                case "all":
                    sessions = sessionController.GetAllSessions();
                    break;
                case "pending":
                    sessions = sessionController.GetAllActiveSessions();
                    break;
                case "needanalysis":
                    sessions = sessionController.GetAllUnanalyzedSessions();
                    break;
                case "complete":
                    sessions = sessionController.GetAllCompletedSessions();
                    break;
                default:
                    sessions = sessionController.GetAllSessions();
                    break;
            }

            List<SessionMinDetails> retVal = new List<SessionMinDetails>();

            foreach (ISession session in sessions)
            {
                SessionMinDetails sessionDetails = new SessionMinDetails
                {
                    Description = session.Description,
                    SessionId = session.SessionId.ToString(),
                    StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status = session.Status,
                    DiagnoserSessions = new List<String>(session.GetDiagnoserSessions().Select(p => p.Diagnoser.Name))
                };

                retVal.Add(sessionDetails);
            }

            return retVal;
        }

        public List<SessionDetails> Get(string type, bool detailed)
        {
            SessionController sessionController = new SessionController();

            IEnumerable<ISession> sessions;
            
            switch(type)
            {
                case "all":
                    sessions = sessionController.GetAllSessions();
                    break;
                case "pending":
                    sessions = sessionController.GetAllActiveSessions();
                    break;
                case "needanalysis":
                    sessions = sessionController.GetAllUnanalyzedSessions();
                    break;
                case "complete":
                    sessions = sessionController.GetAllCompletedSessions();
                    break;
                default:
                    sessions = sessionController.GetAllSessions();
                    break;
            }

            List<SessionDetails> retVal = new List<SessionDetails>();

            foreach (ISession session in sessions)
            {
                SessionDetails sessionDetails = new SessionDetails
                {
                    Description = session.Description,
                    SessionId = session.SessionId.ToString(),
                    StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status = session.Status
                };

                foreach(IDiagnoserSession diagSession in session.GetDiagnoserSessions())
                {
                    DiagnoserSessionDetails diagSessionDetails = new DiagnoserSessionDetails
                    {
                        Name = diagSession.Diagnoser.Name,
                        CollectorStatus = diagSession.CollectorStatus,
                        AnalyzerStatus = diagSession.AnalyzerStatus,
                    };

                    foreach(Log log in diagSession.GetLogs())
                    {
                        LogDetails logDetails = new LogDetails
                        {
                            FileName = log.FileName,
                            RelativePath = log.RelativePath,
                            FullPermanentStoragePath = log.FullPermanentStoragePath,
                            StartTime = log.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            EndTime = log.EndTime.ToString("yyyy-MM-dd HH:mm:ss")
                        };

                        diagSessionDetails.AddLog(logDetails);
                    }

                    foreach (Report report in diagSession.GetReports())
                    {
                        ReportDetails reportDetails = new ReportDetails
                        {
                            FileName = report.FileName,
                            RelativePath = report.RelativePath,
                            FullPermanentStoragePath = report.FullPermanentStoragePath
                        };

                        diagSessionDetails.AddReport(reportDetails);
                    }

                    sessionDetails.AddDiagnoser(diagSessionDetails);
                }

                retVal.Add(sessionDetails);
            }

            return retVal;
        }

        public String Post([FromBody]NewSessionInfo input)
        {
            SessionController sessionController = new SessionController();

            List<Diagnoser> diagnosers = new List<Diagnoser>(sessionController.GetAllDiagnosers().Where(p => input.Diagnosers.Contains(p.Name)));

            String SessionId = "";

            if (input.RunLive)
            {
                TimeSpan ts;
                if (!TimeSpan.TryParse(input.TimeSpan, out ts))
                {
                    ts = TimeSpan.FromMinutes(5);
                }

                if (input.CollectLogsOnly)
                {
                    SessionId = sessionController.CollectLiveDataLogs(ts, diagnosers, null, input.Description).SessionId.ToString();
                }
                else
                {
                    SessionId = sessionController.TroubleshootLiveData(ts, diagnosers, null, input.Description).SessionId.ToString();
                }
            }
            else
            {
                DateTime startTime = DateTime.Parse(input.StartTime);
                DateTime endTime = DateTime.Parse(input.EndTime);
                if (input.CollectLogsOnly)
                {
                    SessionId = sessionController.CollectLogs(startTime, endTime, diagnosers, null, input.Description).SessionId.ToString();
                }
                else
                {
                    SessionId = sessionController.Troubleshoot(startTime, endTime, diagnosers, null, input.Description).SessionId.ToString();
                }
            }

            return SessionId;
        }

        //public void Put(int id, [FromBody]string value)
        //{
        //}

        //public void Delete(int id)
        //{
        //}
    }
}
