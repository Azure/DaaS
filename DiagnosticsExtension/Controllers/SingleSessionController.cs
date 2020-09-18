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
using System.IO;
using System.Threading.Tasks;

namespace DiagnosticsExtension.Controllers
{
    public class SingleSessionController : ApiController
    {
        public HttpResponseMessage Get(string sessionId)
        {
            SessionMinDetails retVal = new SessionMinDetails();
            try
            {
                SessionController sessionController = new SessionController();

                Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

                retVal.Description = session.Description;
                retVal.SessionId = session.SessionId.ToString();
                retVal.StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                retVal.EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss");
                retVal.Status = session.Status;
                retVal.DiagnoserSessions = new List<string>(session.GetDiagnoserSessions().Select(p => p.Diagnoser.Name));
                retVal.HasBlobSasUri = !string.IsNullOrWhiteSpace(session.BlobSasUri);
                retVal.BlobStorageHostName = session.BlobStorageHostName;
            }
            catch (FileNotFoundException fex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, fex.Message);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Encountered exception while getting session", ex, sessionId);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
            
            return Request.CreateResponse(HttpStatusCode.OK, retVal);
        }

        public HttpResponseMessage Get(string sessionId, bool detailed)
        {
            SessionDetails retVal = new SessionDetails();
            try
            {
                SessionController sessionController = new SessionController();

                Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

                retVal.Description = session.Description;
                retVal.SessionId = session.SessionId.ToString();
                retVal.StartTime = session.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
                retVal.EndTime = session.EndTime.ToString("yyyy-MM-dd HH:mm:ss");
                retVal.Status = session.Status;
                retVal.HasBlobSasUri = !string.IsNullOrWhiteSpace(session.BlobSasUri);
                retVal.BlobStorageHostName = session.BlobStorageHostName;

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
                    retVal.LogFilesSize = logFileSize;

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

                    retVal.AddDiagnoser(diagSessionDetails);
                }
            }
            catch (FileNotFoundException fex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, fex.Message);
            }
            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent("Encountered exception while getting session", ex, sessionId);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }

            return Request.CreateResponse(HttpStatusCode.OK, retVal);
        }
        [HttpPost]
        public bool Post(string sessionId, string detailed)
        {
            try
            {
                if (detailed == "downloadreports")
                {
                    SessionController sessionController = new SessionController();
                    Session session = sessionController.GetSessionWithId(new SessionId(sessionId));
                    foreach (DiagnoserSession diagSession in session.GetDiagnoserSessions())
                    {
                        diagSession.DownloadReportsToWebsite();
                    }
                }
                else if (detailed == "startanalysis")
                {
                    SessionController sessionController = new SessionController();
                    Session session = sessionController.GetSessionWithId(new SessionId(sessionId));
                    sessionController.Analyze(session);
                }
                else if (detailed == "cancel")
                {
                    SessionController sessionController = new SessionController();
                    Session session = sessionController.GetSessionWithId(new SessionId(sessionId));
                    sessionController.Cancel(session);
                }
                else
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }


        [HttpDelete]
        public async Task<HttpResponseMessage> Delete(string sessionId)
        {
            SessionController sessionController = new SessionController();
            try
            {
                Session session = null;
                try
                {
                    session = sessionController.GetSessionWithId(new SessionId(sessionId));
                }
                catch (FileNotFoundException ex)
                {
                    return Request.CreateErrorResponse(HttpStatusCode.NotFound, ex.Message);
                }

                bool deleted = await sessionController.Delete(session);
                if (deleted)
                {
                    return Request.CreateResponse(HttpStatusCode.OK, "Data successfully deleted");
                }
                else
                {
                    return Request.CreateErrorResponse(HttpStatusCode.Conflict, "Cannot delete an Active session. Make sure to cancel the session first.");
                }
            }

            catch (Exception ex)
            {
                Logger.LogSessionErrorEvent($"Failed while deleting the session", ex, sessionId);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, $"Failed with error {ex.Message} while trying to delete data for the Session {sessionId}");
            }
        }
    }
}
