using DaaS;
using DaaS.Sessions;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.IO;
using System.Net.Http.Headers;

namespace DiagnosticsExtension.Controllers
{
    public class DownloadFileController : ApiController
    {
        //Referencing Zip code from 
        //https://github.com/projectkudu/kudu/blob/Kudu.Services/Diagnostics/DiagnosticsController.cs
        public HttpResponseMessage Get(string sessionId, string downloadableFileType, string diagnoserName)
        {
            var pathslist = new List<DaaSFileInfo>();
            PopulateFileList(pathslist, sessionId, downloadableFileType, diagnoserName);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (pathslist.Count > 1 || downloadableFileType == "MemoryDumps")
            {
                response.Content = ZipStreamContent.Create(
                    String.Format("{0}-{1}-{2}.zip", sessionId, diagnoserName, downloadableFileType),
                    zip =>
                    {
                        ZipArchiveExtensions.AddFilesToZip(pathslist, zip);
                    });
            }
            else if (pathslist.Count == 0)
            {
                string html = @"<B>DaaS Session Error:</B>
                            <BR/>No files are present. 
                            <BR/>Please try again later if the session is still in progress.
                            <BR/>Please invoke a new session if the session is not in progress.";

                response.Content = new StringContent(html);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/html");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
                response.Content.Headers.ContentDisposition.FileName = "nofiles.html";
  
            }else
            {
                response.Content = new StreamContent(
                    new FileStream(pathslist[0].FilePath, FileMode.Open)
                    );
                FileInfo fi = new FileInfo(pathslist[0].FilePath);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/html");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
                response.Content.Headers.ContentDisposition.FileName = String.Concat(pathslist[0].Prefix, fi.Name);
            }
            return response;
        }


        private void PopulateFileList(List<DaaSFileInfo> pathslist, string sessionId, string downloadableFileType, string diagnoserName)
        {
            SessionController sessionController = new SessionController();

            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            foreach (DiagnoserSession diagSession in session.GetDiagnoserSessions())
            {
                if (downloadableFileType == "Logs")
                {
                    foreach (Log log in diagSession.GetLogs())
                    {
                        if (IsDownloadableFile(log.FileName) && (!diagSession.Diagnoser.Name.StartsWith("Memory Dump", true
                            , culture: System.Globalization.CultureInfo.InvariantCulture)))
                        {
                            if (diagnoserName.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                diagnoserName.StartsWith(diagSession.Diagnoser.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                            {
                                if (!pathslist.Exists(a => a.FilePath == log.FullPermanentStoragePath
                                                           && a.Prefix == GetLogInstanceNamePrefix(log.RelativePath)))
                                {
                                    pathslist.Add(new DaaSFileInfo
                                    {
                                        FilePath = log.FullPermanentStoragePath,
                                        Prefix = GetLogInstanceNamePrefix(log.RelativePath)
                                    });
                                }
                            }
                        }
                    }
                }
                if (downloadableFileType == "Reports")
                {
                    foreach (Report report in diagSession.GetReports())
                    {
                        if (IsDownloadableFile(report.FileName))
                        {
                            if (diagnoserName.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                diagnoserName.StartsWith(diagSession.Diagnoser.Name.Replace(" ", ""),
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                if (!pathslist.Exists(a => a.FilePath == report.FullPermanentStoragePath
                                                        && a.Prefix == String.Empty))
                                {
                                    pathslist.Add(new DaaSFileInfo
                                    {
                                        FilePath = report.FullPermanentStoragePath,
                                        Prefix = String.Empty
                                    });
                                }
                            }
                        }
                    }
                }
                if (downloadableFileType == "MemoryDumps")
                {
                    foreach (Log log in diagSession.GetLogs())
                    {
                        if (IsDownloadableFile(log.FileName)
                            && (diagSession.Diagnoser.Name.StartsWith("Memory Dump"
                            , true, culture: System.Globalization.CultureInfo.InvariantCulture)))
                        {
                            if (diagnoserName.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                                log.FileName.StartsWith(diagnoserName, StringComparison.OrdinalIgnoreCase))
                            {
                                if (
                                    !pathslist.Exists(
                                        a =>
                                            a.FilePath == log.FullPermanentStoragePath &&
                                            a.Prefix == GetLogInstanceNamePrefix(log.RelativePath)))
                                {
                                    pathslist.Add(new DaaSFileInfo
                                       {
                                           FilePath = log.FullPermanentStoragePath,
                                           Prefix = GetLogInstanceNamePrefix(log.RelativePath)
                                       });
                                }
                            }
                        }
                    }
                }

            }
        }

        private string GetLogInstanceNamePrefix(string Path)
        {
            return String.Concat(Path.Split('\\')[3], "_");
        }

        private bool IsDownloadableFile(string fileName)
        {
            return (fileName.EndsWith(".log", true, culture: System.Globalization.CultureInfo.InvariantCulture)
                || fileName.EndsWith(".html", true, culture: System.Globalization.CultureInfo.InvariantCulture)
                || fileName.EndsWith(".dmp", true, culture: System.Globalization.CultureInfo.InvariantCulture)
                || fileName.EndsWith(".mht", true, culture: System.Globalization.CultureInfo.InvariantCulture)
                || fileName.EndsWith(".htm", true, culture: System.Globalization.CultureInfo.InvariantCulture)
                );
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
