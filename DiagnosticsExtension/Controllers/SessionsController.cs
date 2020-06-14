//-----------------------------------------------------------------------
// <copyright file="SessionsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Odbc;
using DaaS.HeartBeats;
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
        public HttpResponseMessage Get(string type)
        {
            List<SessionMinDetails> retVal = new List<SessionMinDetails>();
            try
            {
                SessionController sessionController = new SessionController();
                IEnumerable<Session> sessions;

                switch (type.ToLowerInvariant())
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

                IEnumerable<Session> sortedSessions = sessions.OrderByDescending(p => p.StartTime);

                foreach (Session session in sortedSessions)
                {
                    SessionMinDetails sessionDetails = new SessionMinDetails
                    {
                        Description = session.Description,
                        SessionId = session.SessionId.ToString(),
                        StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Status = session.Status,
                        DiagnoserSessions = new List<String>(session.GetDiagnoserSessions().Select(p => p.Diagnoser.Name)),
                        HasBlobSasUri = !string.IsNullOrWhiteSpace(session.BlobSasUri)
                    };

                    retVal.Add(sessionDetails);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Encountered exception while getting sessions from file", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Encountered exception {ex.Message} while getting sessions from file");

            }


            return Request.CreateResponse(HttpStatusCode.OK, retVal); 
        }

        public HttpResponseMessage Get(string type, bool detailed)
        {
            List<SessionDetails> retVal = new List<SessionDetails>();

            try
            {
                SessionController sessionController = new SessionController();

                IEnumerable<Session> sessions;

                switch (type.ToLowerInvariant())
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

                IEnumerable<Session> sortedSessions = sessions.OrderByDescending(p => p.StartTime);

                foreach (Session session in sortedSessions)
                {
                    SessionDetails sessionDetails = new SessionDetails
                    {
                        Description = session.Description,
                        SessionId = session.SessionId.ToString(),
                        StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Status = session.Status,
                        HasBlobSasUri = !string.IsNullOrWhiteSpace(session.BlobSasUri)
                    };

                    foreach (DiagnoserSession diagSession in session.GetDiagnoserSessions())
                    {
                        DiagnoserSessionDetails diagSessionDetails = new DiagnoserSessionDetails
                        {
                            Name = diagSession.Diagnoser.Name,
                            CollectorStatus = diagSession.CollectorStatus,
                            AnalyzerStatus = diagSession.AnalyzerStatus,
                            CollectorStatusMessages = diagSession.CollectorStatusMessages,
                            AnalyzerStatusMessages = diagSession.AnalyzerStatusMessages
                        };

                        foreach (String analyzerError in diagSession.GetAnalyzerErrors())
                        {
                            diagSessionDetails.AddAnalyzerError(analyzerError);
                        }

                        foreach (String collectorError in diagSession.GetCollectorErrors())
                        {
                            diagSessionDetails.AddCollectorError(collectorError);
                        }

                        int minLogRelativePathSegments = Int32.MaxValue;

                        double logFileSize = 0;
                        foreach (Log log in diagSession.GetLogs())
                        {
                            logFileSize += log.FileSize;
                            LogDetails logDetails = new LogDetails
                            {
                                FileName = log.FileName,
                                RelativePath = log.RelativePath,
                                FullPermanentStoragePath = log.FullPermanentStoragePath,
                                StartTime = log.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
                                EndTime = log.EndTime.ToString("yyyy-MM-dd HH:mm:ss")
                            };

                            int relativePathSegments = logDetails.RelativePath.Split('\\').Length;

                            if (relativePathSegments == minLogRelativePathSegments)
                            {
                                logDetails.RelativePath = logDetails.RelativePath.Replace('\\', '/');
                                diagSessionDetails.AddLog(logDetails);
                            }
                            else if (relativePathSegments < minLogRelativePathSegments)
                            {
                                minLogRelativePathSegments = relativePathSegments;
                                logDetails.RelativePath = logDetails.RelativePath.Replace('\\', '/');
                                diagSessionDetails.ClearReports();
                                diagSessionDetails.AddLog(logDetails);
                            }
                        }
                        sessionDetails.LogFilesSize = logFileSize;

                        int minReportRelativePathSegments = Int32.MaxValue;
                        foreach (Report report in diagSession.GetReports())
                        {
                            ReportDetails reportDetails = new ReportDetails
                            {
                                FileName = report.FileName,
                                RelativePath = report.RelativePath,
                                FullPermanentStoragePath = report.FullPermanentStoragePath
                            };

                            int relativePathSegments = reportDetails.RelativePath.Split('\\').Length;

                            if (relativePathSegments == minReportRelativePathSegments)
                            {
                                reportDetails.RelativePath = reportDetails.RelativePath.Replace('\\', '/');
                                diagSessionDetails.AddReport(reportDetails);
                            }
                            else if (relativePathSegments < minReportRelativePathSegments)
                            {
                                minReportRelativePathSegments = relativePathSegments;
                                reportDetails.RelativePath = reportDetails.RelativePath.Replace('\\', '/');
                                diagSessionDetails.ClearReports();
                                diagSessionDetails.AddReport(reportDetails);
                            }
                        }

                        sessionDetails.AddDiagnoser(diagSessionDetails);
                    }

                    retVal.Add(sessionDetails);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Encountered exception while getting sessions from file", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Encountered exception {ex.Message} while getting sessions from file");
            }

            return Request.CreateResponse(HttpStatusCode.OK, retVal);
        }


        public HttpResponseMessage Post([FromBody]NewSessionInfo input)
        {
            //Simulate Delay
            //System.Threading.Thread.Sleep(20000);

            //Simulate Failure
            //return "";

            String SessionId = String.Empty;
            string InstancesSubmitted = "";
            try
            {
                SessionController sessionController = new SessionController();
                sessionController.StartSessionRunner();

                List<Diagnoser> diagnosers = new List<Diagnoser>(sessionController.GetAllDiagnosers().Where(p => input.Diagnosers.Contains(p.Name)));
                List<Instance> instances = new List<Instance>();

                instances = input.Instances.Any() ? new List<Instance>(sessionController.GetAllRunningSiteInstances().Where(p => input.Instances.Contains(p.Name)))
                                                  : new List<Instance>(sessionController.GetAllRunningSiteInstances());

                InstancesSubmitted = string.Join(",", instances.Select(x => x.Name));

                TimeSpan ts;
                if (!TimeSpan.TryParse(input.TimeSpan, out ts))
                {
                    ts = TimeSpan.FromMinutes(5);
                }

                if (input.RunLive)
                {
                    if (input.CollectLogsOnly)
                    {
                        SessionId = sessionController.CollectLiveDataLogs(ts, diagnosers, false, instances, input.Description, input.BlobSasUri).SessionId.ToString();
                    }
                    else
                    {
                        SessionId = sessionController.TroubleshootLiveData(ts, diagnosers, false, instances, input.Description, input.BlobSasUri).SessionId.ToString();
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(input.StartTime))
                    {
                        throw new ArgumentNullException("When RunLive is false or not specified, StartTime must be provided");
                    }

                    DateTime startTime = DateTime.Parse(input.StartTime);
                    //startTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc);
                    if (startTime > DateTime.UtcNow)
                    {
                        DateTime endTime = startTime + ts;
                        if (input.CollectLogsOnly)
                        {
                            SessionId = sessionController.CollectLogs(startTime, endTime, diagnosers, false, null, input.Description, input.BlobSasUri).SessionId.ToString();
                        }
                        else
                        {
                            SessionId = sessionController.Troubleshoot(startTime, endTime, diagnosers, false, null, input.Description, input.BlobSasUri).SessionId.ToString();
                        }
                    }
                    else
                    {
                        if (input.CollectLogsOnly)
                        {
                            SessionId = sessionController.CollectLiveDataLogs(ts, diagnosers, false, instances, input.Description, input.BlobSasUri).SessionId.ToString();
                        }
                        else
                        {
                            SessionId = sessionController.TroubleshootLiveData(ts, diagnosers, false, instances, input.Description, input.BlobSasUri).SessionId.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //log to Kusto and re-throw (it will be taken care in support API)
                Logger.LogErrorEvent("Encountered exception while submitting session", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Encountered exception {ex.Message} while submitting session");
            }

            return Request.CreateResponse(HttpStatusCode.OK, SessionId);
        }

    }
}
