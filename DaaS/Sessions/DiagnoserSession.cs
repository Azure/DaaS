//-----------------------------------------------------------------------
// <copyright file="DiagnoserSession.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.HeartBeats;
using DaaS.Storage;

namespace DaaS.Sessions
{
    enum SessionType
    {
        Collect,
        Diagnose
    }

    public enum DiagnosisStatus
    {
        NotRequested,
        WaitingForInputs,
        InProgress,
        Error,
        Cancelled,
        Complete,
    }

    public class DiagnoserStatusMessage
    {
        public string EntityType;
        public string Message;
    }

    public class DiagnoserSession
    {
        public Diagnoser Diagnoser { get; private set; }
        public DiagnosisStatus CollectorStatus { get; set; }
        public List<DiagnoserStatusMessage> CollectorStatusMessages
        {
            get
            {
                return _collectorStatusMessages;
            }
        }
        public List<DiagnoserStatusMessage> AnalyzerStatusMessages
        {
            get
            {
                return _analyzerStatusMessages;
            }
        }
        public DiagnosisStatus AnalyzerStatus { get; set; }
        public DateTime AnalyzerStartTime { get; set; } = DateTime.MinValue.ToUniversalTime();

        private List<DiagnoserStatusMessage> _collectorStatusMessages = new List<DiagnoserStatusMessage>();
        private List<DiagnoserStatusMessage> _analyzerStatusMessages = new List<DiagnoserStatusMessage>();
        private readonly List<Instance> _instancesCollected = new List<Instance>();

        private int _collectorFailureCount = 0;
        private int _analyzerFailureCount = 0;

        private int _collectorRunCount = 0;
        private int _analyzerRunCount = 0;

        public int NumberOfInstancesCollected
        {
            get
            {
                return _logReports.Keys.GroupBy(l => l.GetInstanceName()).Count();
            }
        }

        private List<Instance> InstancesCollected
        {
            get
            {
                foreach (var report in _logReports.Keys.GroupBy(l => l.GetInstanceName()))
                {
                    Instance instanceToAdd = new Instance(report.Key);
                    if (!_instancesCollected.Contains(instanceToAdd))
                    {
                        _instancesCollected.Add(instanceToAdd);
                    }
                }

                return _instancesCollected;
            }
        }

        private readonly Dictionary<Log, List<Report>> _logReports = new Dictionary<Log, List<Report>>();

        private List<string> _collectorErrors = new List<string>();
        private List<string> _analyzerErrors = new List<string>();

        internal DiagnoserSession(Diagnoser diagnoser, SessionType sessionType)
        {
            Diagnoser = diagnoser;

            switch (sessionType)
            {
                case SessionType.Collect:
                    CollectorStatus = DiagnosisStatus.InProgress;
                    AnalyzerStatus = DiagnosisStatus.NotRequested;
                    break;
                case SessionType.Diagnose:
                    CollectorStatus = DiagnosisStatus.InProgress;
                    AnalyzerStatus = DiagnosisStatus.WaitingForInputs;
                    break;
            }
        }

        internal DiagnoserSession(XElement diagnoserXml)
        {
            LoadDiagnoserFromXml(diagnoserXml);
        }

        internal bool ContainsLog(Log log)
        {
            return _logReports.ContainsKey(log);
        }

        internal bool ContainsReport(Report report)
        {
            foreach (var reports in _logReports.Values)
            {
                if (reports.Contains(report))
                {
                    return true;
                }
            }
            return false;
        }

        public void MarkInstanceAsRun(Instance instance)
        {
            if (!InstancesCollected.Contains(instance))
            {
                InstancesCollected.Add(instance);
            }
        }

        internal List<Instance> GetCollectedInstances()
        {
            return InstancesCollected;
        }

        public bool HasThisInstanceRunTheCollector()
        {
            Logger.LogDiagnostic("Checking if this instance Run the collector - {0} ", Settings.InstanceName);
            if (_logReports.Keys.Any(l => l.RelativePath.Contains(Settings.InstanceName)))
            {
                Logger.LogDiagnostic("This instance ran the collector - {0} ", Settings.InstanceName);
                return true;
            }
            Logger.LogDiagnostic("This instance did not run the collector - {0} ", Settings.InstanceName);
            return false;
        }

        internal bool AllRequiredInstancesHaveRunTheCollector(List<Instance> instancesToCollect)
        {
            if (instancesToCollect == null || instancesToCollect.Count == 0)
            {
                int numberOfLiveInstances = HeartBeatController.GetNumberOfLiveInstances();
                Logger.LogDiagnostic("All instances need to run the collector");
                return numberOfLiveInstances <= NumberOfInstancesCollected;
            }

            var collectedInstances = GetCollectedInstances();

            Logger.LogDiagnostic("Checking if Instances to collect list is subset of Already Collected instances");
            if (instancesToCollect.All(collectedInstances.Contains))
            {
                Logger.LogDiagnostic("We done with collector on specified instances");
                return true;
            }

            var liveInstances = HeartBeatController.GetLiveInstances();

            // If an instance is no longer live don't wait for it to run its collector
            var instancesThatCanBeCollected = instancesToCollect.Intersect(liveInstances);

            if (instancesThatCanBeCollected.Any(instanceToCollect => !collectedInstances.Contains(instanceToCollect)))
            {
                Logger.LogDiagnostic("Not all instances ran collector");
                return false;
            }
            Logger.LogDiagnostic("We done with collector on specified instances");
            return true;
        }

        public IEnumerable<Log> GetLogs()
        {
            return _logReports.Keys;
        }

        public void AddLog(Log log)
        {
            if (!ContainsLog(log))
            {
                _logReports.Add(log, new List<Report>());
            }
        }

        public IEnumerable<Report> GetReports()
        {
            List<Report> allReports = new List<Report>();
            foreach (var reports in _logReports.Values)
            {
                allReports.AddRange(reports);
            }
            return allReports;
        }

        public void AddReportsForLog(Log log, List<Report> reports)
        {
            _logReports[log].AddRange(reports);
        }

        private void AddReportForLog(Log log, Report report)
        {
            _logReports[log].Add(report);
        }

        public bool HasUnanalyzedLogs()
        {
            return _logReports.Keys.Any(log => _logReports[log].Count == 0);
        }

        public bool IsAnalyzerRunningForLongTime()
        {
            if (AnalyzerStartTime != DateTime.MinValue.ToUniversalTime())
            {
                return DateTime.UtcNow.Subtract(AnalyzerStartTime).TotalMinutes >= (Infrastructure.Settings.MaxAnalyzerTimeInMinutes);
            }
            else
            {
                // Analyzer isn't even started yet
                return false;
            }

        }

        public IEnumerable<Log> GetUnanalyzedLogs()
        {
            return _logReports.Keys.Where(log => _logReports[log].Count == 0 && log.AnalysisStarted == DateTime.MinValue.ToUniversalTime()).ToList();
        }

        public IEnumerable<Report> GetReportsForLog(Log log)
        {
            List<Report> reports;
            if (!_logReports.TryGetValue(log, out reports))
            {
                reports = new List<Report>();
            }
            return reports;
        }

        internal void LogCollectorFailure(Exception e)
        {
            _collectorFailureCount++;
            if (e != null)
            {
                _collectorErrors.Add(string.Format("{0} - {1}", e.GetType(), e.Message));
            }
            Logger.LogErrorEvent($"{Diagnoser.Collector.Name} encountered a failure and Collector failure count is {_collectorFailureCount}", e);
        }

        internal void LogAnalyzerFailure(Exception e)
        {
            _analyzerFailureCount++;
            if (e != null)
            {
                _analyzerErrors.Add(string.Format("{0} - {1}", e.GetType(), e.Message));
            }
            Logger.LogErrorEvent($"{Diagnoser.Analyzer.Name} encountered a failure and analyzer failure count is {_analyzerFailureCount}", e);
        }

        public List<string> GetCollectorErrors()
        {
            return _collectorErrors;
        }

        public List<string> GetAnalyzerErrors()
        {
            return _analyzerErrors;
        }

        internal void LogNewCollectorRun()
        {
            _collectorRunCount++;
        }

        internal void LogNewAnalyzerRun()
        {
            _analyzerRunCount++;
        }

        internal bool IsCollectorHealthy()
        {
            return HasItRetriedTooManyTimes(_collectorRunCount) && IsFailureCountTooHigh(_collectorFailureCount);
        }

        internal bool IsAnalyzerHealthy()
        {
            return HasItRetriedTooManyTimes(_analyzerRunCount, true) && IsFailureCountTooHigh(_analyzerFailureCount, true);
        }

        private bool HasItRetriedTooManyTimes(int runCount, bool dontNormalizePerInstance = false)
        {
            return IsCountTooHigh(runCount, "Run", dontNormalizePerInstance);
        }

        private bool IsFailureCountTooHigh(int failureCount, bool dontNormalizePerInstance = false)
        {
            return IsCountTooHigh(failureCount, "Failure", dontNormalizePerInstance);
        }

        private bool IsCountTooHigh(int count, string countType, bool dontNormalizePerInstance = false)
        {
            Logger.LogDiagnostic("Checking {0} count", countType);
            int normalizedCount = NormalizeCountPerInstance(count);
            if (dontNormalizePerInstance)
            {
                normalizedCount = count;
            }
            int maxRetries = Infrastructure.Settings.MaxDiagnosticToolRetryCount;
            Logger.LogDiagnostic(
                "{3} count = {0}, Normalized {3} count = {1}, Max retry count = {2}",
                count,
                normalizedCount,
                maxRetries,
                countType);
            return normalizedCount <= maxRetries;
        }

        private int NormalizeCountPerInstance(int count)
        {
            return count / HeartBeatController.GetNumberOfLiveInstances();
        }

        internal XElement GetXml()
        {
            var collectorXml = new XElement(
                SessionXml.Collector,
                    new XAttribute(SessionXml.Status, CollectorStatus),
                    new XAttribute(SessionXml.RunCount, _collectorRunCount),
                    new XAttribute(SessionXml.FailureCount, _collectorFailureCount)
                );

            foreach (var error in _collectorErrors)
            {
                collectorXml.Add(new XElement(SessionXml.Error, error));
            }

            var logsXml = new XElement(SessionXml.Logs);
            foreach (var logReport in _logReports)
            {
                var logXml = new XElement(SessionXml.Log,
                                new XAttribute(SessionXml.Path, logReport.Key.RelativePath),
                                new XAttribute(SessionXml.AnalysisStarted, logReport.Key.AnalysisStarted.ToUniversalTime().ToString(SessionXml.TimeFormat)),
                                new XAttribute(SessionXml.FileSize, logReport.Key.FileSize),
                                new XAttribute(SessionXml.BlobSasUri, logReport.Key.BlobSasUri),
                                new XAttribute(SessionXml.InstanceAnalyzing, logReport.Key.InstanceAnalyzing));

                foreach (var report in logReport.Value)
                {
                    logXml.Add(new XElement(
                        SessionXml.Report,
                        new XAttribute(SessionXml.Path, report.RelativePath)
                        ));
                }

                logsXml.Add(logXml);
            }

            var instanceXml = new XElement(
                SessionXml.InstancesCollected
                );
            foreach (var instance in InstancesCollected.Distinct())
            {
                instanceXml.Add(new XElement(
                    SessionXml.Instance,
                    instance.Name));
            }

            var analyzerXml = new XElement(
                SessionXml.Analyzer,
                    new XAttribute(SessionXml.Status, AnalyzerStatus),
                    new XAttribute(SessionXml.RunCount, _analyzerRunCount),
                    new XAttribute(SessionXml.FailureCount, _analyzerFailureCount),
                    new XAttribute(SessionXml.AnalyzerStartTime, AnalyzerStartTime.ToUniversalTime().ToString(SessionXml.TimeFormat))
                );

            foreach (var error in _analyzerErrors)
            {
                analyzerXml.Add(new XElement(SessionXml.Error, error));
            }

            var diagnoserXml =
                new XElement(SessionXml.Diagnoser,
                    new XAttribute(SessionXml.Name, Diagnoser.Name),
                    instanceXml,
                    collectorXml,
                    analyzerXml,
                    logsXml
                );

            return diagnoserXml;
        }

        private void LoadDiagnoserFromXml(XElement diagnoserXml)
        {
            var diagnoserName = diagnoserXml.Attribute(SessionXml.Name).Value;
            var allDiagnosers = Infrastructure.Settings.GetDiagnosers();
            Diagnoser =
                allDiagnosers.FirstOrDefault(d => d.Name.Equals(diagnoserName, StringComparison.OrdinalIgnoreCase));
            if (Diagnoser == null)
            {
                // The settings file has changed so that this diagnoser no longer exists.
                // We can no longer analyze this session but we'll create a mock diagnoser that no-ops on everything
                Diagnoser = new Diagnoser()
                {
                    Name = diagnoserName,
                    Collector = new RangeCollector()
                    {
                        Arguments = "",
                        Command = "",
                        Name = ""
                    },
                    Analyzer = new Analyzer()
                    {
                        Arguments = "",
                        Command = "",
                        Name = "MockAnalyzer"
                    }
                };
            }

            var collectorXml = diagnoserXml.Element(SessionXml.Collector);
            var analyzerXml = diagnoserXml.Element(SessionXml.Analyzer);

            DiagnosisStatus collectorStatus;
            DiagnosisStatus.TryParse(
                collectorXml.Attribute(SessionXml.Status).Value,
                ignoreCase: true,
                result: out collectorStatus);
            CollectorStatus = collectorStatus;

            if (collectorStatus == DiagnosisStatus.InProgress)
            {
                CheckAndReturnCollectorDetailedStatus(diagnoserXml);
            }
            var collectorRunCountXml = collectorXml.Attribute(SessionXml.RunCount);
            if (collectorRunCountXml != null && !string.IsNullOrEmpty(collectorRunCountXml.Value))
            {
                _collectorRunCount = int.Parse(collectorRunCountXml.Value);
            }
            _collectorFailureCount = int.Parse(collectorXml.Attribute(SessionXml.FailureCount).Value);

            foreach (var errorXml in collectorXml.Elements(SessionXml.Error))
            {
                _collectorErrors.Add(errorXml.Value);
            }

            DiagnosisStatus analyzerStatus;
            DiagnosisStatus.TryParse(
                analyzerXml.Attribute(SessionXml.Status).Value,
                ignoreCase: true,
                result: out analyzerStatus);
            AnalyzerStatus = analyzerStatus;

            if (analyzerXml.Attribute(SessionXml.AnalyzerStartTime) != null)
            {
                if (DateTime.TryParse(analyzerXml.Attribute(SessionXml.AnalyzerStartTime).Value, out DateTime analyzerStartTime))
                {
                    AnalyzerStartTime = analyzerStartTime.ToUniversalTime();
                }
            }

            var analyzerRunCountXml = analyzerXml.Attribute(SessionXml.RunCount);
            if (analyzerRunCountXml != null && !string.IsNullOrEmpty(analyzerRunCountXml.Value))
            {
                _analyzerRunCount = int.Parse(analyzerRunCountXml.Value);
            }
            _analyzerFailureCount = int.Parse(analyzerXml.Attribute(SessionXml.FailureCount).Value);

            foreach (var errorXml in analyzerXml.Elements(SessionXml.Error))
            {
                _analyzerErrors.Add(errorXml.Value);
            }

            var logsXml = diagnoserXml.Element(SessionXml.Logs);
            if (logsXml != null)
            {
                foreach (var logXml in logsXml.Elements(SessionXml.Log))
                {
                    var logPath = logXml.Attribute(SessionXml.Path).Value;

                    double fileSize = 0;
                    if (logXml.Attribute(SessionXml.FileSize) != null)
                    {
                        double.TryParse(logXml.Attribute(SessionXml.FileSize).Value, out fileSize);
                    }

                    string blobSasUri = string.Empty;
                    if (logXml.Attribute(SessionXml.BlobSasUri) != null)
                    {
                        blobSasUri = logXml.Attribute(SessionXml.BlobSasUri).Value;
                    }

                    var log = Log.GetLogFromPermanentStorage(logPath, fileSize, blobSasUri);

                    if (logXml.Attribute(SessionXml.AnalysisStarted) != null)
                    {
                        if (DateTime.TryParse(logXml.Attribute(SessionXml.AnalysisStarted).Value, out DateTime analysisStarted))
                        {
                            log.AnalysisStarted = analysisStarted.ToUniversalTime();
                        }
                    }
                    if (logXml.Attribute(SessionXml.InstanceAnalyzing) != null)
                    {
                        log.InstanceAnalyzing = logXml.Attribute(SessionXml.InstanceAnalyzing).Value;
                    }

                    AddLog(log);

                    if (analyzerStatus == DiagnosisStatus.InProgress)
                    {
                        CheckAndReturnAnalyzerDetailedStatus(log);
                    }
                    foreach (var reportXml in logXml.Elements(SessionXml.Report))
                    {
                        var reportPath = reportXml.Attribute(SessionXml.Path).Value;
                        var report = Report.GetReport(reportPath);
                        AddReportForLog(log, report);
                    }
                }
            }
        }

        private void CheckAndReturnAnalyzerDetailedStatus(Log log)
        {
            var path = Path.Combine(
               "Reports",
               Infrastructure.Settings.SiteNameShort,
               log.EndTime.ToString("yy-MM-dd"),
               log.EndTime.ToString(SessionConstants.SessionFileNameFormat),
               Diagnoser.Analyzer.Name);

            string fullDirPath = Path.Combine(EnvironmentVariables.DaasPath, path);

            var files = new List<string>();
            if (Directory.Exists(fullDirPath))
            {
                files = Directory.GetFiles(fullDirPath, log.FileName + ".diagstatus.diaglog", SearchOption.TopDirectoryOnly).ToList();
                //Logger.LogDiagnostic("Found {0} status file in path  {1}", files.Count, fullDirPath);
            }

            var logFileName = log.FileName;
            logFileName = Path.GetFileNameWithoutExtension(logFileName);

            foreach (var statusFile in files)
            {
                try
                {
                    using (FileStream fs = System.IO.File.Open(statusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (StreamReader sr = new StreamReader(fs))
                        {
                            while (!sr.EndOfStream)
                            {
                                var msg = new DiagnoserStatusMessage
                                {
                                    EntityType = logFileName,
                                    Message = sr.ReadLine()
                                };
                                AnalyzerStatusMessages.Add(msg);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogErrorEvent($"Failed to read status file for Analyzer", ex);
                }
            }
        }

        private void CheckAndReturnCollectorDetailedStatus(XElement diagnoserXml)
        {
            if (diagnoserXml.Parent != null)
            {
                var diagnosers = diagnoserXml.Parent;

                if (diagnosers.Parent != null)
                {
                    var sessionXml = diagnosers.Parent;
                    var timeRangeXml = sessionXml.Element(SessionXml.TimeRange);
                    var StartTime = DateTime.Parse(timeRangeXml.Element(SessionXml.StartTime).Value).ToUniversalTime();
                    var EndTime = DateTime.Parse(timeRangeXml.Element(SessionXml.EndTime).Value).ToUniversalTime();

                    foreach (var instance in HeartBeatController.GetLiveInstances())
                    {
                        var path = Path.Combine("Logs",
                                                Infrastructure.Settings.SiteNameShort,
                                                EndTime.ToString("yy-MM-dd"),
                                                instance.Name,
                                                this.Diagnoser.Collector.Name,
                                                StartTime.ToString(SessionConstants.SessionFileNameFormat));

                        string fullDirPath = Path.Combine(EnvironmentVariables.DaasPath, path);
                        var files = new List<string>();
                        if (Directory.Exists(fullDirPath))
                        {
                            files = Directory.GetFiles(fullDirPath, "diagstatus.diaglog", SearchOption.TopDirectoryOnly).ToList();
                        }

                        foreach (var statusFile in files)
                        {
                            try
                            {
                                using (FileStream fs = System.IO.File.Open(statusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    using (StreamReader sr = new StreamReader(fs))
                                    {
                                        while (!sr.EndOfStream)
                                        {
                                            var msg = new DiagnoserStatusMessage
                                            {
                                                EntityType = instance.Name,
                                                Message = sr.ReadLine()
                                            };
                                            CollectorStatusMessages.Add(msg);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogErrorEvent($"Failed to read status file", ex);
                            }
                        }
                    }
                }
            }
        }

        internal void MergeUpdates(DiagnoserSession diagnoserSessionToMerge)
        {
            this.CollectorStatus =
                (DiagnosisStatus)Math.Max(
                    (int)this.CollectorStatus,
                    (int)diagnoserSessionToMerge.CollectorStatus);

            this.AnalyzerStatus =
                (DiagnosisStatus)Math.Max(
                    (int)this.AnalyzerStatus,
                    (int)diagnoserSessionToMerge.AnalyzerStatus);

            if (diagnoserSessionToMerge.AnalyzerStartTime != DateTime.MinValue.ToUniversalTime())
            {
                if (AnalyzerStartTime == DateTime.MinValue.ToUniversalTime())
                {
                    AnalyzerStartTime = diagnoserSessionToMerge.AnalyzerStartTime;
                }
                if (diagnoserSessionToMerge.AnalyzerStartTime < AnalyzerStartTime)
                {
                    AnalyzerStartTime = diagnoserSessionToMerge.AnalyzerStartTime;
                }
            }

            // Merge the logs and reports
            foreach (var logReportToMerge in diagnoserSessionToMerge._logReports)
            {
                var newLog = logReportToMerge.Key;
                if (!this._logReports.ContainsKey(newLog))
                {
                    this._logReports[newLog] = logReportToMerge.Value;
                }
                else
                {
                    var reports = this._logReports[logReportToMerge.Key];
                    foreach (var newReport in logReportToMerge.Value)
                    {
                        if (!reports.Contains(newReport))
                        {
                            reports.Add(newReport);
                        }
                    }
                }
            }

            // merge the AnalysisStarted and AnalysisCancelled propery for every Log object
            foreach (var remotelog in diagnoserSessionToMerge.GetLogs())
            {
                foreach (var currentLog in this.GetLogs().Where(x => x.InstanceAnalyzing != Environment.MachineName))
                {
                    if (remotelog.RelativePath == currentLog.RelativePath)
                    {
                        if (remotelog.AnalysisStarted != DateTime.MinValue.ToUniversalTime())
                        {
                            currentLog.AnalysisStarted = remotelog.AnalysisStarted;
                            Logger.LogDiagnostic($"Merging AnalysisStarted of remote log {remotelog.RelativePath} with {remotelog.InstanceAnalyzing}  with the current one");
                        }
                    }
                }
            }
        }

        public void DownloadReportsToWebsite()
        {
            foreach (var report in GetReports())
            {
                Infrastructure.Storage.CopyFileToLocation(report, StorageLocation.UserSiteData);
            }
        }

        internal bool IsComplete()
        {
            return CollectorStatus == DiagnosisStatus.Complete && AnalyzerStatus == DiagnosisStatus.Complete;
        }

        internal bool IsErrorState()
        {
            return CollectorStatus == DiagnosisStatus.Error || AnalyzerStatus == DiagnosisStatus.Error;
        }

        internal bool IsCollectedLogsOnlyState()
        {
            return CollectorStatus == DiagnosisStatus.Complete && AnalyzerStatus == DiagnosisStatus.NotRequested;
        }

        internal bool IsCancelled()
        {
            return CollectorStatus == DiagnosisStatus.Cancelled || AnalyzerStatus == DiagnosisStatus.Cancelled;
        }
    }
}
