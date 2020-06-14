//-----------------------------------------------------------------------
// <copyright file="SingleSessionController.cs" company="Microsoft Corporation">
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
    public class SingleSessionController : ApiController
    {
        public SessionMinDetails Get(string sessionId)
        {
            SessionController sessionController = new SessionController();

            ISession session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

            SessionMinDetails retval = new SessionMinDetails
            {
                Description = session.Description,
                SessionId = session.SessionId.ToString(),
                StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Status = session.Status,
                DiagnoserSessions = new List<string>(session.GetDiagnoserSessions().Select(p => p.Diagnoser.Name))
            };

            return retval;
        }

        public SessionDetails Get(string sessionId, bool detailed)
        {
            SessionController sessionController = new SessionController();

            ISession session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

            SessionDetails retVal = new SessionDetails
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

                retVal.AddDiagnoser(diagSessionDetails);
            }

            return retVal;
        }

        public bool Post(string sessionId)
        {
            try
            {
                SessionController sessionController = new SessionController();

                Session session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

                sessionController.Analyze(session);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // PUT api/values/5
        //public void Put(int id, [FromBody]string value)
        //{
        //}

        // DELETE api/values/5
        //public void Delete(int id)
        //{
        //}
    }
}
