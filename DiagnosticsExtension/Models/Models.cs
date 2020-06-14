//-----------------------------------------------------------------------
// <copyright file="Models.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DaaS;
using DaaS.Diagnostics;
using DaaS.Sessions;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Web;
using Newtonsoft.Json;

namespace DiagnosticsExtension.Models
{
    public class DiagnoserDetails
    {
        [DataMember]
        public string Name;

        [DataMember]
        public List<string> Warnings;

        [DataMember]
        public string Description;

        public DiagnoserDetails(Diagnoser diagnoser)
        {
            Name = diagnoser.Name;
            Warnings = new List<string>(diagnoser.GetWarnings());
            Description = diagnoser.Description;
        }
    }


    public class SessionDetailsList
    {
        [DataMember]
        public List<SessionDetails> Sessions = new List<SessionDetails>();

        public void AddSession(SessionDetails sessionInfo)
        {
            Sessions.Add(sessionInfo);
        }
    }

    public class SessionMinDetails
    {
        [DataMember]
        public string Description;
        [DataMember]
        public string SessionId;
        [DataMember]
        public string StartTime;
        [DataMember]
        public string EndTime;
        [DataMember]
        public SessionStatus Status;
        [DataMember]
        public List<String> DiagnoserSessions = new List<String>();
        [DataMember]
        public bool HasBlobSasUri;

        public void AddDiagnoser(String diagnoserName)
        {
            DiagnoserSessions.Add(diagnoserName);
        }
    }

    public class SessionDetails
    {
        [DataMember]
        public string Description;
        [DataMember]
        public string SessionId;
        [DataMember]
        public string StartTime;
        [DataMember]
        public string EndTime;
        [DataMember]
        public double LogFilesSize;
        [DataMember]
        public SessionStatus Status;
        [DataMember]
        public bool HasBlobSasUri;
        [DataMember]
        public List<DiagnoserSessionDetails> DiagnoserSessions = new List<DiagnoserSessionDetails>();

        public void AddDiagnoser(DiagnoserSessionDetails diagnoserSessionDetails)
        {
            DiagnoserSessions.Add(diagnoserSessionDetails);
        }
    }

    public class DiagnoserSessionMinDetails
    {
        [DataMember(Name = "Name")]
        public string Name;
        [DataMember(Name = "CollectorStatus")]
        public DiagnosisStatus CollectorStatus;
        [DataMember(Name = "AnalyzerStatus")]
        public DiagnosisStatus AnalyzerStatus;
        [DataMember(Name = "Logs")]
        public List<String> Logs = new List<String>();
        [DataMember(Name = "Reports")]
        public List<String> Reports = new List<String>();
        public void AddLog(String logFileName)
        {
            Logs.Add(logFileName);
        }

        public void ClearLogs()
        {
            Logs.Clear();
        }
        public void AddReport(String reportFileName)
        {
            Reports.Add(reportFileName);
        }

        public void ClearReports()
        {
            Reports.Clear();
        }
    }

    public class DiagnoserSessionDetails
    {
        [DataMember(Name = "Name")]
        public String Name;
        [DataMember(Name = "CollectorStatus")]
        public DiagnosisStatus CollectorStatus;
        [DataMember(Name = "CollectorStatusMessages")]
        public List<DiagnoserStatusMessage> CollectorStatusMessages;
        [DataMember(Name = "AnalyzerStatus")]
        public DiagnosisStatus AnalyzerStatus;
        [DataMember(Name = "AnalyzerStatusMessages")]
        public List<DiagnoserStatusMessage> AnalyzerStatusMessages;
        [DataMember(Name = "CollectorErrors")]
        public List<String> CollectorErrors = new List<String>();
        [DataMember(Name = "AnalyzerErrors")]
        public List<String> AnalyzerErrors = new List<String>();
        [DataMember(Name = "Logs")]
        public List<LogDetails> Logs = new List<LogDetails>();
        [DataMember(Name = "Reports")]
        public List<ReportDetails> Reports = new List<ReportDetails>();
        public void AddCollectorError(String collectorError)
        {
            CollectorErrors.Add(collectorError);
        }

        public void ClearCollectorErrors()
        {
            CollectorErrors.Clear();
        }
        public void AddAnalyzerError(String analyzerError)
        {
            AnalyzerErrors.Add(analyzerError);
        }

        public void ClearAnalyzerErrors()
        {
            AnalyzerErrors.Clear();
        }
        public void AddLog(LogDetails logDetails)
        {
            Logs.Add(logDetails);
        }

        public void ClearLogs()
        {
            Logs.Clear();
        }

        public void AddReport(ReportDetails reportDetails)
        {
            Reports.Add(reportDetails);
        }

        public void ClearReports()
        {
            Reports.Clear();
        }
    }

    public class LogDetails
    {
        [DataMember(Name = "Filename")]
        public string FileName;
        [DataMember(Name = "RelativePath")]
        public string RelativePath;
        [DataMember(Name = "FullPermanentStoragePath")]
        public string FullPermanentStoragePath;
        [DataMember(Name = "StartTime")]
        public string StartTime;
        [DataMember(Name = "EndTime")]
        public string EndTime;
    }

    public class ReportDetails
    {
        [DataMember(Name = "Filename")]
        public string FileName;
        [DataMember(Name = "RelativePath")]
        public string RelativePath;
        [DataMember(Name = "FullPermanentStoragePath")]
        public string FullPermanentStoragePath;
    }

    public class NewSessionInfo
    {
        [DataMember]
        public bool RunLive;

        [DataMember]
        public bool CollectLogsOnly;
        
        [DataMember]
        public string StartTime;
        
        [DataMember(IsRequired = true)]
        public string TimeSpan;
        
        [DataMember(IsRequired = true)]
        public List<string> Diagnosers;
        
        [DataMember]
        public List<string> Instances;
        
        [DataMember]
        public string Description;

        [DataMember]
        public string BlobSasUri;

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }
    }

    public class Settings
    {
        [DataMember]
        public List<String> Diagnosers;
        [DataMember]
        public string TimeSpan;
        [DataMember]
        public string BlobSasUri;
        [DataMember]
        public string BlobContainer;
        [DataMember]
        public string BlobKey;
        [DataMember]
        public string BlobAccount;
        [DataMember]
        public string EndpointSuffix;
    }

    public class PrivateSetting
    {
        [DataMember]
        public string Name;
        [DataMember]
        public string Value;
    }

    public class DownloadableFile
    {
        [DataMember(Name = "FileDisplayName")]
        public string FileDisplayName;
        [DataMember(Name = "Path")]
        public string Path;
        [DataMember(Name = "FileType")]
        public string FileType;
        [DataMember(Name = "Status")]
        public string Status;
        [DataMember(Name = "FileSize")]
        public string FileSize;
        [DataMember(Name = "DirectFilePath")]
        public string DirectFilePath;
    }

}
