// -----------------------------------------------------------------------
// <copyright file="DownloadFilesListController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class DownloadFilesListController : ApiController
    {
        public List<DownloadableFile> Get(string sessionId, string downloadableFileType)
        {
            var defaultFileList = "Reports";
            List<DownloadableFile> retval;
            switch (downloadableFileType)
            {
                case "Reports":
                case "Logs":
                case "MemoryDumps":
                    retval = GetFileList(ref sessionId, ref downloadableFileType);
                    break;
                default:
                    retval = GetFileList(ref sessionId, ref defaultFileList);
                    break;
            }
            return retval;
        }

        private List<DownloadableFile> GetFileList(ref string sessionId, ref string downloadableFileType)
        {
            var downloadablefiles = new List<DownloadableFile>();
            SessionController sessionController = new SessionController();
            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            var scmHostName = GetScmHostName(session);
            foreach (DiagnoserSession diagSession in session.GetDiagnoserSessions())
            {
                if (downloadableFileType == "Logs" && String.CompareOrdinal(diagSession.Diagnoser.Name, "Memory Dump") > 0)
                {
                    var trimmedDiagnoserName = diagSession.Diagnoser.Name.Replace(" ", "");
                    downloadablefiles.Add(new DownloadableFile
                            {
                                FileDisplayName = trimmedDiagnoserName,
                                Path = String.Concat(
                                        "https://",
                                        scmHostName,
                                        "/daas/api/v2/session/downloadfile/"
                                        , session.SessionId
                                        , "/Logs/"
                                        , trimmedDiagnoserName
                                        ),
                                FileType = "Logs",
                                Status = System.Enum.GetName(typeof(DiagnosisStatus), diagSession.CollectorStatus)
                            });
                }

                if (downloadableFileType == "Reports")
                {
                    var trimmedDiagnoserName = diagSession.Diagnoser.Name.Replace(" ", "");
                    downloadablefiles.Add(new DownloadableFile
                    {
                        FileDisplayName = trimmedDiagnoserName,
                        Path = String.Concat(
                                        "https://",
                                        scmHostName,
                                        "/daas/api/v2/session/downloadfile/"
                                        , session.SessionId
                                        , "/Reports/",
                                        trimmedDiagnoserName
                                        ),
                        FileType = "Reports",
                        Status = System.Enum.GetName(typeof(DiagnosisStatus), diagSession.AnalyzerStatus)
                    });

                }
                if (downloadableFileType == "MemoryDumps")
                {
                    downloadablefiles.AddRange(from log in diagSession.GetLogs()
                                               where log.FileName.ToLower().EndsWith(".dmp")
                                               let relativePaths = log.RelativePath.Split('\\')
                                               select new DownloadableFile
                                               {
                                                   FileDisplayName = String.Concat(relativePaths[3], "_", log.FileName.Split('_')[0]),
                                                   DirectFilePath = String.Concat("https://",
                                                                     scmHostName,
                                                                     "/api/vfs/data/DaaS/",
                                                                     log.RelativePath.Replace('\\', '/')
                                                                   ),
                                                   FileType = "MemoryDump",
                                                   Status = "Ready",
                                                   FileSize = DaaS.ConversionUtils.BytesToString(new FileInfo(log.FullPermanentStoragePath).Length),
                                                   Path = String.Concat("https://",
                                                                     scmHostName,
                                                                     "/daas/api/v2/session/downloadfile/",
                                                                     session.SessionId,
                                                                     "/MemoryDumps/",
                                                                     log.FileName.Replace(".dmp", String.Empty))
                                               });
                }
            }
            return downloadablefiles;
        }

        private string GetScmHostName(Session session)
        {
            var scmHostName = String.Empty;
            try
            {
                var hostName = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME").ToLowerInvariant();
                var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME").ToLowerInvariant();

                if (hostName != null)
                {
                    scmHostName = hostName.Replace(siteName + ".", siteName + ".scm.");
                }
            }
            catch (Exception)
            {
                scmHostName = String.Concat(session.SiteName.ToLowerInvariant(), ".scm.azurewebsites.net");
            }
            return scmHostName;
        }

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

    }
}
