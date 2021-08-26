// -----------------------------------------------------------------------
// <copyright file="Analyzer.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.Leases;
using DaaS.Sessions;
using DaaS.Storage;

namespace DaaS.Diagnostics
{
    class Analyzer : DiagnosticTool
    {
        public override string Name { get; internal set; }
        public string Command { get; set; }
        public string Arguments { get; set; }
        internal async Task<List<Report>> Analyze(Log log, string sessionId, string blobSasUri, string defaultHostName, CancellationToken ct)
        {
            // Get a lease to analzye this particular log
            Lease lease = Infrastructure.LeaseManager.TryGetLease(log.RelativePath, blobSasUri);
            if (!Lease.IsValid(lease))
            {
                return null;
            }

            // Run the analyzer command
            var outputDir = CreateTemporaryReportDestinationFolder(log, defaultHostName);

            await RunAnalyzerAsync(lease, log, outputDir, sessionId, ct);
            
            // Store the reports to blob storage
            var reports = MoveReportsToPermanentStorage(outputDir);
            if (reports == null || reports.Count == 0 || reports.Any(x => x.FileName.EndsWith(".err.diaglog")))
            {
                string analyzerException = string.Empty;
                if (reports.Any(x => x.FileName.EndsWith(".err.diaglog")))
                {
                    var report = reports.FirstOrDefault(x => x.FileName.EndsWith(".err.diaglog"));
                    string errorFileName = report.FullPermanentStoragePath;
                    if (System.IO.File.Exists(errorFileName))
                    {
                        analyzerException = System.IO.File.ReadAllText(errorFileName);
                    }
                }
                throw new DiagnosticToolHasNoOutputException(Name, analyzerException);
            }

            // Return the reports (we'll just let the lease expire on its own so that we can hold on to it until the reports have been logged in the session)
            return  reports.Where(x => !x.FileName.EndsWith(".diaglog")).ToList();
        }

        private async Task RunAnalyzerAsync(Lease lease, Log log, string outputDir, string sessionId, CancellationToken ct)
        {
            await log.CacheLogInTempFolderAsync();

            Logger.LogSessionVerboseEvent($"Cached log file {log.FileName} in TempFolder", sessionId);

            var args = ExpandVariablesInArgument(log, outputDir);

            CancellationTokenSource analyzerTimeoutCts = new CancellationTokenSource();
            analyzerTimeoutCts.CancelAfter(TimeSpan.FromMinutes(Infrastructure.Settings.MaxAnalyzerTimeInMinutes));
            var combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(ct, analyzerTimeoutCts.Token);

            await Task.Run(() => RunProcessWhileKeepingLeaseAliveAsync(lease, Command, args, sessionId, combinedCancellationSource.Token), combinedCancellationSource.Token);
        }

        private string CreateTemporaryReportDestinationFolder(Log log, string defaultHostName)
        {
            var destinationDir = Path.Combine(
                "Reports",
                defaultHostName,
                log.StartTime.ToString(SessionConstants.SessionFileNameFormat),
                Name);
            var tempDir = Infrastructure.Storage.GetNewTempFolder(destinationDir);
            return tempDir;
        }

        protected string ExpandVariablesInArgument(Log log, string outputDir)
        {
            var variables = new Dictionary<string, string>()
            {
                {"logFile", Path.Combine(Infrastructure.Settings.TempDir, log.RelativePath.ConvertForwardSlashesToBackSlashes())},
                {"outputDir", outputDir}
            };
            var args = ExpandVariables(Arguments, variables);
            return args;
        }

        protected List<Report> MoveReportsToPermanentStorage(string outputDir)
        {
            // Once collector finishes executing, move log to permanent storage
            List<string> reportsGenerated = Infrastructure.Storage.GetFilesInDirectory(outputDir, StorageLocation.TempStorage, string.Empty);
            List<Report> reports = new List<Report>();
            List<Task> saveReportTasks = new List<Task>();
            foreach (var reportFile in reportsGenerated)
            {
                var relativeFilePath = reportFile.Replace(Infrastructure.Settings.TempDir + "\\", "");
                var report = Report.GetReport(relativeFilePath);
                reports.Add(report);
                var saveTask = Infrastructure.Storage.SaveFileAsync(report, report.StorageLocation);
                saveReportTasks.Add(saveTask);
            }

            Task.WaitAll(saveReportTasks.ToArray());
            return reports;
        }
    }
}
