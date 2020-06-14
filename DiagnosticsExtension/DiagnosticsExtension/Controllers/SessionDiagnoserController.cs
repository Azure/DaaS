//-----------------------------------------------------------------------
// <copyright file="SessionDiagnoserController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

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

            ISession session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

            IDiagnoserSession diagSession = session.GetDiagnoserSessions().Where(p => p.Diagnoser.Name == diagnoser).First();

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

            ISession session = sessionController.GetSessionWithId(new SessionId(sessionId)).Result;

            IDiagnoserSession diagSession = session.GetDiagnoserSessions().Where(p => p.Diagnoser.Name == diagnoser).First();

            DiagnoserSessionDetails retVal = new DiagnoserSessionDetails
            {
                Name = diagSession.Diagnoser.Name,
                CollectorStatus = diagSession.CollectorStatus,
                AnalyzerStatus = diagSession.AnalyzerStatus
            };
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
                retVal.AddLog(temp);
            }
            foreach (Report report in diagSession.GetReports())
            {
                ReportDetails temp = new ReportDetails
                {
                    FileName = report.FileName,
                    RelativePath = report.RelativePath,
                    FullPermanentStoragePath = report.FullPermanentStoragePath
                };
                retVal.AddReport(temp);
            }

            return retVal;
        }
    }
}
