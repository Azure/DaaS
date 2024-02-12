// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClrProfilingAnalyzer.Parser;
using Diagnostics.Tracing.StackSources;
using DiagnosticsHub.Packaging.Interop;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.DiagnosticsHub.Packaging.InteropEx;
using Newtonsoft.Json;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System.Runtime.InteropServices;
using System.IO.Compression;
using DaaS;

namespace ClrProfilingAnalyzer
{
    class SimpleTreeNode
    {
        public string FunctionName;
        public float TimeSpent;
        public float ExclusiveTime;
        public List<SimpleTreeNode> childNodes;
        public float InclusiveMetricPercent;
    }

    class Program
    {
        const int MIN_REQUEST_DURATION_IN_MILLISECONDS = 500;
        const int MAX_SLOWEST_REQUESTS_TO_DISPLAY = 100;
        private static readonly string[] ExcludedModulesForLoadingSymbols = new string[]
        {
           "authanon", "bcrypt","bcryptprimitives","cachfile","cachhttp"," cachuri","CLBCatQ","cachtokn", "cachrui","iisreqs","iphlpapi",
            "clrcompression", "clrjit", "combase","compdyn", "compstat", "cryptsp","custerr","defdoc","diasymreader","iisfcgi", "iisfreb",
            "diprestr","dirlist", "gzip","iis_ssi","iisetw", "iprestr", "loghttp","msvcp140.i386", "msvcr120.i386","ncrypt", "cryptbase",
            "ncryptsslp","OnDemandConnRouteHelper","protsup", "rsaenh","sechost","shlwapi","static", "ucrtbase", "ucrtbase_clr0400.i386",
            "vcruntime140.i386", "vcruntime140_clr0400.i386", "version", "wgdi32", "wgdi32full", "wrpcrt4", "wsspicli","wuser32","wwin32u",
            "modrqflt","msvcp140.i386", "msvcp_win", "ole32", "oleaut32", "shcore", "schannel", "ucrtbase_clr0400.i386", "warmup", "wgdi32full",
            "aspnet_filter", "cachuri", "cgi", "filter", "msvcr120.i386", " msvcr110.i386","nativerd","redirect", "validcfg"
        };
        static int m_ProcessId = 0;
        static string m_OutputPath = "";
        static string m_OutputPathWithInstanceName = "";
        static string m_ReportDataPath = "";
        static string m_DiagSessionPath = "";
        static string m_StackTracerOutput = "";
        static string m_symbolFilePath = "";
        static string m_DiagSessionZipFilePath = "";
        static double g_CPUTimeTotalMetrics = 0;
        static double g_CPUMetricPerInterval = 0;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetDiskFreeSpaceEx(string path, out ulong freeBytes, out ulong totalBytes, out ulong diskFreeBytes);

        static bool GetDiskFreeSpace(string path, out ulong freeBytes, out ulong totalBytes)
        {
            ulong diskFreeBytes;
            if (GetDiskFreeSpaceEx(path, out freeBytes, out totalBytes, out diskFreeBytes))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static string GetLocalFilePath(string packageFilePath, DhPackage package, ResourceInfo resource, string fileExtension)
        {
            string localFileName = Path.GetFileNameWithoutExtension(resource.Name);
            string localFilePath = packageFilePath + "_" + localFileName + fileExtension;

            try
            {
                Logger.LogDiagnoserVerboseEvent($"Extracting to file {localFilePath}");
                if (!File.Exists(localFilePath))
                {
                    package.ExtractResourceToPath(ref resource.ResourceId, localFilePath);
                }
            }
            catch (Exception ex)
            {
                string diskUsageDetails = GetDiskUsageDetails();
                Logger.LogDiagnoserVerboseEvent($"Failed while extracting ETL Trace. LocalFilePath is {localFilePath} and Disk Usage Details are {diskUsageDetails} and Exception {ex.GetType().ToString()}:{ex.Message} \r\n {ex.StackTrace}");
                throw ex;
            }

            return localFilePath;
        }

        private static string GetDiskUsageDetails()
        {
            string diskUsageDetails = string.Empty;
            try
            {
                var homePath = Environment.GetEnvironmentVariable("HOME");
                var localPath = EnvironmentVariables.Local;

                if (GetDiskFreeSpace(localPath, out ulong freeBytes, out ulong totalBytes))
                {
                    var usageLocal = Math.Round(((totalBytes - freeBytes) * 100.0) / totalBytes);
                    diskUsageDetails = $" {localPath} has totalBytes = [{totalBytes.ToString()}]  and freeBytes = [{freeBytes.ToString()}] and UsedPercent = {usageLocal.ToString()} ";
                }

                if (GetDiskFreeSpace(homePath, out freeBytes, out totalBytes))
                {
                    var usageHomePath = Math.Round(((totalBytes - freeBytes) * 100.0) / totalBytes);
                    diskUsageDetails += $", {homePath} has totalBytes = [{totalBytes.ToString()}]  and freeBytes = [{freeBytes.ToString()}] and UsedPercent = {usageHomePath.ToString()} ";
                }
            }
            catch (Exception)
            {
            }

            return diskUsageDetails;
        }

        static void Main(string[] args)
        {
            m_DiagSessionPath = args[0];
            m_DiagSessionZipFilePath = args[0];
            m_OutputPath = args[1];

            var kustoLoggingDisabled = false;

            if (args.Length > 2)
            {
                if (args[2] == "nolog")
                {
                    kustoLoggingDisabled = true;
                }
            }

            Logger.Init(m_DiagSessionZipFilePath, m_OutputPath, "ClrProfilingAnalyzer", false);
            Logger.KustoLoggingDisabled = kustoLoggingDisabled;

            ClrProfilingAnalyzerStats stats = new ClrProfilingAnalyzerStats
            {
                StatsType = "ClrProfilingAnalyzer",
                ActivityId = Logger.ActivityId,
                SiteName = Logger.SiteName
            };

            try
            {
                if (!IsValidFile())
                {
                    return;
                }
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                string etlFilePath = ExtractEtlFromDiagSession(out string uncompressedPath);

                Logger.LogDiagnoserVerboseEvent($"Opening Trace File {etlFilePath}");

                if (string.IsNullOrWhiteSpace(etlFilePath))
                {
                    throw new Exception("Failed to extract ETW trace from the diagsession file. Please check diaglog file for more details");
                }

                TraceLog dataFile = TraceLog.OpenOrConvert(etlFilePath);
                Logger.LogDiagnoserVerboseEvent($"Trace File {etlFilePath} opened");

                string fileName = Path.GetFileNameWithoutExtension(m_DiagSessionPath);
                m_ProcessId = ExtractProcessIdFromEtlFileName(fileName);

                if (m_ProcessId == 0)
                {
                    Logger.TraceFatal($"Failed to identify the process ID from the Trace File {fileName}");
                    return;
                }

                m_OutputPathWithInstanceName = Path.Combine(m_OutputPath, dataFile.MachineName);
                Directory.CreateDirectory(m_OutputPathWithInstanceName);

                Dictionary<Guid, IisRequest> iisRequests = new Dictionary<Guid, IisRequest>();

                stopWatch.Stop();
                stats.TimeToOpenTraceFileInSeconds = stopWatch.Elapsed.TotalSeconds;

                stopWatch.Restart();
                Logger.LogStatus("Parsing IIS events from the trace file");
                iisRequests = IisRequestParser.ParseIISEvents(dataFile, m_ProcessId, iisRequests);

                Logger.LogDiagnoserVerboseEvent($"Finished Parsing IIS Events, found {iisRequests.Count()} requests");
                stopWatch.Stop();
                stats.TimeToParseIISEventsInSeconds = stopWatch.Elapsed.TotalSeconds;

                stopWatch.Restart();
                Logger.LogStatus("Parsing .NET Core events from the trace file");

                var coreParserResults = AspNetCoreRequestParser.ParseDotNetCoreRequests(dataFile, MIN_REQUEST_DURATION_IN_MILLISECONDS);

                Logger.LogDiagnoserVerboseEvent($"Finished Parsing .NET Core Events, found {coreParserResults.Requests.Count()} requests");
                stopWatch.Stop();
                stats.TimeToParseNetCoreEventsInSeconds = stopWatch.Elapsed.TotalSeconds;
                stats.SlowRequestCountAspNetCore = coreParserResults.Requests.Count();
                stats.FailedRequestCountAspNetCore = coreParserResults.FailedRequests.Count();

                var containsIisEvents = IisRequestParser.ContainsIisEvents;

                var slowRequests = iisRequests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderByDescending(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).Take(MAX_SLOWEST_REQUESTS_TO_DISPLAY).Where((m => (m.EndTimeRelativeMSec - m.StartTimeRelativeMSec) > MIN_REQUEST_DURATION_IN_MILLISECONDS));

                m_ReportDataPath = Path.Combine(m_OutputPathWithInstanceName, "reportdata");
                Directory.CreateDirectory(m_ReportDataPath);

                CopyStaticContent(m_OutputPath, dataFile.MachineName);

                stopWatch.Restart();
                var symbolReader = LoadSymbols(dataFile);
                stopWatch.Stop();
                stats.TimeToLoadSymbolsInSeconds = stopWatch.Elapsed.TotalSeconds;

                if (containsIisEvents)
                {
                    stopWatch.Restart();
                    GenerateStackTracesForSlowRequests(symbolReader, dataFile, slowRequests, stats);
                    stopWatch.Stop();
                    stats.TimeToGenerateStackTraces = stopWatch.Elapsed.TotalSeconds;
                    DumpSlowRequests(slowRequests, stats);
                }
                else
                {
                    // need to create an empty requests.json so that the report is happy
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "requests.json")))
                    {
                        file.WriteLine("[]");
                    }
                }

                if (coreParserResults.Requests.Count > 0)
                {
                    stopWatch.Restart();
                    var slowAspnetCoreRequests = coreParserResults.Requests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderByDescending(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).Take(MAX_SLOWEST_REQUESTS_TO_DISPLAY).Where((m => (m.EndTimeRelativeMSec - m.StartTimeRelativeMSec) > MIN_REQUEST_DURATION_IN_MILLISECONDS));
                    GenerateStackTracesForSlowRequestsAspNetCore(symbolReader, dataFile, slowAspnetCoreRequests, stats);
                    stopWatch.Stop();
                    stats.TimeToGenerateStackTracesAspNetCore = stopWatch.Elapsed.TotalSeconds;
                    DumpAspNetCoreSlowRequests(slowAspnetCoreRequests, coreParserResults.AspNetCoreRequestsFullTrace);
                }
                else
                {
                    // need to create an empty requests.json so that the report is happy
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "corerequests.json")))
                    {
                        file.WriteLine("[]");
                    }
                }

                if (coreParserResults.FailedRequests.Count > 0)
                {
                    stopWatch.Restart();
                    DumpAspNetCoreFailedRequests(coreParserResults.FailedRequests);
                    stopWatch.Stop();
                    stats.TimeToDumpFailedCoreRequests = stopWatch.Elapsed.TotalSeconds;
                }
                else
                {
                    // need to create an empty requests.json so that the report is happy
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "corefailedrequests.json")))
                    {
                        file.WriteLine("[]");
                    }
                }

                stopWatch.Restart();
                Logger.LogStatus("Generating CPU Stacks");
                GenerateCPUStackTraces(dataFile, coreParserResults.Processes, false);
                stopWatch.Stop();
                stats.TimeToGenerateCpuStacks = stopWatch.Elapsed.TotalSeconds;

                stopWatch.Restart();
                DumpTraceInformation(iisRequests, dataFile, stats, containsIisEvents, coreParserResults.Processes);
                stopWatch.Stop();
                stats.TimeToLoadTraceInfoInSeconds = stopWatch.Elapsed.TotalSeconds;

                stopWatch.Restart();
                Logger.LogStatus("Analyzing failed requests in the trace");
                int failedRequestsWithClrExceptions = 0;
                List<ExceptionSummaryByName> exceptionSummary = null;
                List<IisRequestInfo> listRequestsFailed = IisRequestParser.ParseClrExceptions(dataFile, iisRequests, out failedRequestsWithClrExceptions, out exceptionSummary);
                stopWatch.Stop();
                stats.TimeToGenerateParseClrExceptions = stopWatch.Elapsed.TotalSeconds;
                stats.FailedRequestCount = listRequestsFailed.Count();
                stats.FailedRequestsWithClrExceptions = failedRequestsWithClrExceptions;

                if (!string.IsNullOrWhiteSpace(m_StackTracerOutput))
                {
                    File.Copy(m_StackTracerOutput, Path.Combine(m_ReportDataPath, "stacks.json"));
                }

                using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "failedrequests.json")))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, listRequestsFailed);
                }

                var exceptionsWithMessage = exceptionSummary.Where(x => x.ProcessName.Equals("w3wp", StringComparison.OrdinalIgnoreCase)).ToList();
                if (exceptionsWithMessage.Count > 0)
                {
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "allexceptions.json")))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Serialize(file, exceptionsWithMessage);
                    }
                }

                var exceptionsWithMessageOutProc = exceptionSummary.Where(x => !x.ProcessName.Equals("w3wp", StringComparison.OrdinalIgnoreCase)).ToList();
                if (exceptionsWithMessageOutProc.Count > 0)
                {
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "allexceptionsoutproc.json")))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Serialize(file, exceptionsWithMessageOutProc);
                    }
                }
                stats.OutProcClrExceptions = exceptionsWithMessageOutProc.Count;

                Logger.TraceStats(JsonConvert.SerializeObject(stats));

                CleanupExtractedFiles(etlFilePath, uncompressedPath);
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while analyzing the trace", ex);
                Logger.TraceFatal($"Failed while analyzing the trace with exception - {ex.GetType()}: {ex.Message}", false);
            }
        }

        private static void CleanupExtractedFiles(string etlFilePath, string uncompressedPath)
        {
            try
            {
                FileSystemHelpers.DeleteFile(etlFilePath);
                Logger.LogDiagnoserVerboseEvent($"Deleted file {etlFilePath}");

                if (!string.IsNullOrWhiteSpace(uncompressedPath))
                {
                    DeleteDirectory(uncompressedPath);
                    Logger.LogDiagnoserVerboseEvent($"Deleted directory {uncompressedPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserWarningEvent("Failed while cleaning up the extracted file", ex);
            }
        }

        private static void DumpAspNetCoreFailedRequests(Dictionary<AspNetCoreRequest, List<AspNetCoreTraceEvent>> failedRequests)
        {
            try
            {
                using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "corefailedrequests.json")))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, failedRequests.Keys.ToArray());
                }

                foreach (var failedRequest in failedRequests)
                {
                    string fileName = failedRequest.Key.ActivityId + "-failed-detailed.json";
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, fileName)))
                    {
                        JsonSerializer serializer = new JsonSerializer
                        {
                            Formatting = Formatting.Indented
                        };
                        serializer.Serialize(file, failedRequest.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while dumping ASP.NET Core requests", ex);
            }
        }

        private static void DumpAspNetCoreSlowRequests(IEnumerable<AspNetCoreRequest> aspNetCoreRequests, Dictionary<AspNetCoreRequestId, List<AspNetCoreTraceEvent>> requestsWithFullTrace)
        {
            try
            {
                using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "corerequests.json")))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, aspNetCoreRequests);
                }

                foreach (var slowRequest in requestsWithFullTrace)
                {
                    string fileName = slowRequest.Key.ActivityId + "-detailed.json";
                    using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, fileName)))
                    {
                        JsonSerializer serializer = new JsonSerializer
                        {
                            Formatting = Formatting.Indented
                        };
                        serializer.Serialize(file, slowRequest.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while dumping ASP.NET Core requests", ex);
            }
        }

        private static void GenerateStackTracesForSlowRequestsAspNetCore(SymbolReader symbolReader, TraceLog dataFile, IEnumerable<AspNetCoreRequest> aspNetCoreRequests, ClrProfilingAnalyzerStats stats)
        {
            MutableTraceEventStackSource mutStacksAsync = new MutableTraceEventStackSource(dataFile)
            {
                ShowUnknownAddresses = false
            };
#pragma warning disable 618
            var computerAsync = new ThreadTimeStackComputer(dataFile, symbolReader);
#pragma warning restore 618 

            computerAsync.ExcludeReadyThread = true;
            computerAsync.UseTasks = true;
            computerAsync.GroupByStartStopActivity = true;

            bool shouldParseAsync = true;

            try
            {
                computerAsync.GenerateThreadTimeStacks(mutStacksAsync);
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while generating activity stacks for .net core", ex);
                shouldParseAsync = false;
            }

            int counter = 0;
            foreach (var request in aspNetCoreRequests)
            {
                counter++;
                shouldParseAsync = shouldParseAsync && !string.IsNullOrWhiteSpace(request.ShortActivityId);
                if (shouldParseAsync)
                {
                    FilterParams filterParamsAsync = new FilterParams()
                    {
                        StartTimeRelativeMSec = request.StartTimeRelativeMSec.ToString(),
                        EndTimeRelativeMSec = request.EndTimeRelativeMSec.ToString(),
                        IncludeRegExs = request.ShortActivityId.ToString(),
                        FoldRegExs = "ntoskrnl!%ServiceCopyEnd;System.Runtime.CompilerServices.Async%MethodBuilder;^STARTING TASK"
                    };

                    bool hasActivityStack = false;
                    double executionTime = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;
                    hasActivityStack = GenerateStackTraces(mutStacksAsync, filterParamsAsync, request.ActivityId.ToString(), false, executionTime, true, true);
                    hasActivityStack = GenerateStackTraces(mutStacksAsync, filterParamsAsync, request.ActivityId.ToString(), true, executionTime, true, true);
                    request.HasActivityStack = hasActivityStack;
                }

                Logger.LogInfo(string.Format("Generated Stack Trace for .Net Core Request {0}. {1}", counter.ToString(), request.Path));

            }
        }

        internal static void CopyStaticContent(string outputReportPath, string machineName)
        {
            string outputPathWithInstance = Path.Combine(outputReportPath, machineName);

            string staticContentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "staticcontent");

            foreach (var dirPath in Directory.GetDirectories(staticContentPath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(staticContentPath, outputPathWithInstance));
            }

            foreach (var newPath in Directory.GetFiles(staticContentPath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(staticContentPath, outputPathWithInstance), true);
            }

            string redirectUrl = machineName + "/index.html";

            string placeHolderhtml = $@"<!DOCTYPE HTML>
                                        <html lang='en - US'>
                                              <head>
                                                  <meta charset = 'UTF-8'>
                                                   <meta http - equiv = 'refresh' content = '1; url={redirectUrl}' >
                                                        <script type = 'text/javascript' >
                                                             window.location.href = '{redirectUrl}'
                                                         </script >
                                                         <title > Page Redirection </title >
                                                        </head>
                                                        <body>                          
                                                If you are not redirected automatically, follow this < a href = '{redirectUrl}' > link to example</a>.
                                                 </body>
                                             </html>";

            string placeHolderFileName = Path.Combine(outputReportPath, $"{machineName}_w3wp_{m_ProcessId}.html");
            File.WriteAllText(placeHolderFileName, placeHolderhtml);
        }




        private static void DumpSlowRequests(IEnumerable<IisRequest> slowRequests, ClrProfilingAnalyzerStats stats)
        {
            List<IisRequestInfo> listRequests = new List<IisRequestInfo>();
            List<IisPipelineEvent> slowestPipelineEvents = new List<IisPipelineEvent>();

            foreach (var request in slowRequests)
            {
                IisRequestInfo iisRequest = new IisRequestInfo();

                iisRequest.Method = request.Method;
                iisRequest.ContextId = request.ContextId;
                iisRequest.slowestPipelineEvent = IisRequestParser.GetSlowestEvent(request);
                slowestPipelineEvents.Add(iisRequest.slowestPipelineEvent);
                iisRequest.totalTimeSpent = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;
                iisRequest.requestPath = request.Path;
                iisRequest.csBytes = (request.BytesReceived == 0) ? "-" : request.BytesReceived.ToString();
                iisRequest.scBytes = (request.BytesSent == 0) ? "-" : request.BytesSent.ToString();
                iisRequest.statusCode = (request.StatusCode == 0) ? "-" : request.StatusCode.ToString();
                iisRequest.SubStatusCode = request.SubStatusCode.ToString();
                iisRequest.FailureDetails = request.FailureDetails;
                iisRequest.HasActivityStack = request.HasActivityStack;
                iisRequest.HasThreadStack = request.HasThreadStack;
                iisRequest.RelatedActivityId = request.RelatedActivityId;
                listRequests.Add(iisRequest);
            }

            Logger.LogStatus("Dumping information about slow requests");
            using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "requests.json")))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, listRequests);
            }

            stats.SlowRequestCount = listRequests.Count();
            stats.SlowestPipelineEvents = slowestPipelineEvents;
        }

        private static bool IsValidFile()
        {
            // TODO: This is Required so we dont end up analyzing the ClrProfilingCollector.Diaglog
            // Once we ensure that DaaS is coded to ignore files of certain extensions, we will
            // remove this logic. Right now it is required to keept DaaS happy !!!
            Logger.LogDiagnoserVerboseEvent($"m_DiagSessionPath = {m_DiagSessionPath}");
            if (!m_DiagSessionPath.EndsWith(".zip"))
            {
                Logger.LogDiagnoserVerboseEvent($"Ignoring file {m_DiagSessionPath} as it is not a zip file");
                string logsDir = Path.Combine(m_OutputPath, "logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                using (StreamWriter file = File.CreateText(Path.Combine(logsDir, $"NoOp_{Path.GetFileName(m_DiagSessionPath).Replace("_ClrProfilingCollector", "")}.log")))
                {
                    file.WriteLine($"Ignoring file {m_DiagSessionPath} as it is not a zip file");
                }
                return false;
            }
            else
            {
                string newFolderName = Path.Combine(Path.GetDirectoryName(m_DiagSessionPath), Path.GetFileNameWithoutExtension(m_DiagSessionPath));
                if (FileSystemHelpers.DirectoryExists(newFolderName))
                {
                    FileSystemHelpers.DeleteDirectoryContentsSafe(newFolderName);
                    FileSystemHelpers.DeleteDirectorySafe(newFolderName);
                }

                Logger.LogDiagnoserVerboseEvent($"Extracting {m_DiagSessionPath} to {newFolderName}");
                ZipFile.ExtractToDirectory(m_DiagSessionPath, newFolderName);

                var extractedFiles = Directory.EnumerateFiles(newFolderName);
                if (extractedFiles.Any(x => x.ToLower().EndsWith(".diagsession")))
                {
                    m_DiagSessionPath = extractedFiles.FirstOrDefault(x => x.ToLower().EndsWith(".diagsession"));

                    if (extractedFiles.Any(x => x.ToLower().EndsWith("stacks.json")))
                    {
                        m_StackTracerOutput = extractedFiles.FirstOrDefault(x => x.ToLower().EndsWith("stacks.json"));
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }

        }

        private static void DumpTraceInformation(Dictionary<Guid, IisRequest> iisRequests, TraceLog dataFile, ClrProfilingAnalyzerStats stats, bool containsIisEvents, List<AspNetCoreProcess> coreProcesses)
        {
            bool traceHasRequests = iisRequests.Count() > 0;
            int fiftyPercentile = 0, ninetyPercentile = 0, ninetyFifthPercentile = 0;
            stats.TotalRequestCount = iisRequests.Count();

            if (traceHasRequests)
            {
                GetPercentiles(iisRequests, out fiftyPercentile, out ninetyPercentile, out ninetyFifthPercentile);
                stats.FiftyPercentile = fiftyPercentile;
                stats.NinetyPercentile = ninetyPercentile;
                stats.NinetyFifthPercentile = ninetyFifthPercentile;
            }

            double totalTimeInRequestExecution = iisRequests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderByDescending(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).Take(MAX_SLOWEST_REQUESTS_TO_DISPLAY).Where((m => (m.EndTimeRelativeMSec - m.StartTimeRelativeMSec) > MIN_REQUEST_DURATION_IN_MILLISECONDS)).Sum(x => (x.EndTimeRelativeMSec - x.StartTimeRelativeMSec));

            var moduleExecutionPercent = GetModuleExcecutionPercent(iisRequests, totalTimeInRequestExecution);

            TraceInfo etlTraceInformation = new TraceInfo
            {
                NumberOfProcessors = dataFile.NumberOfProcessors,
                TotalRequests = iisRequests.Count(),
                TraceDuration = dataFile.SessionDuration.TotalSeconds,
                SuccessfulRequests = traceHasRequests ? iisRequests.Values.Where(x => x.StatusCode <= 399).Count() : 0,
                FailedRequests = traceHasRequests ? iisRequests.Values.Where(x => x.StatusCode > 399).Count() : 0,
                IncompleteRequests = traceHasRequests ? iisRequests.Values.Where(x => x.BytesSent == 0).Count() : 0,
                AverageResponseTime = traceHasRequests ? iisRequests.Values.Average(x => (x.EndTimeRelativeMSec - x.StartTimeRelativeMSec)) : 0,
                FiftyPercentile = fiftyPercentile,
                NinetyPercentile = ninetyPercentile,
                NinetyFifthPercentile = ninetyFifthPercentile,
                ModuleExecutionPercent = moduleExecutionPercent,
                TotalTimeInRequestExecution = Math.Round(totalTimeInRequestExecution),
                InstanceName = dataFile.MachineName,
                TraceFileName = Path.GetFileName(m_DiagSessionZipFilePath),
                TraceFileLocation = GetUrlForTraceFile(m_DiagSessionZipFilePath),
                TraceStartTime = dataFile.SessionStartTime.ToString(),
                ContainsIisEvents = containsIisEvents
            };

            stats.InstanceName = dataFile.MachineName;
            stats.IncompleteRequestCount = etlTraceInformation.IncompleteRequests;
            stats.TraceFileName = etlTraceInformation.TraceFileName;
            stats.AverageResponseTime = etlTraceInformation.AverageResponseTime;
            stats.ModuleExecutionPercent = moduleExecutionPercent;
            stats.TraceDuration = dataFile.SessionDuration.TotalSeconds;

            foreach (var process in dataFile.Processes)
            {
                if (process.ProcessID == m_ProcessId)
                {
                    etlTraceInformation.CPUTimeByThisProcess = process.CPUMSec;
                    etlTraceInformation.CPUTimeTotalMetrics = g_CPUTimeTotalMetrics;
                    etlTraceInformation.CPUMetricPerInterval = g_CPUMetricPerInterval;
                    etlTraceInformation.ProcessId = m_ProcessId;
                    stats.CPUTimeByThisProcess = etlTraceInformation.CPUTimeByThisProcess;
                    stats.ProcessId = m_ProcessId;
                    stats.PercentCPUMachine = etlTraceInformation.CPUMetricPerInterval * 100;
                    stats.PercentCPUProcess = (process.CPUMSec / etlTraceInformation.CPUTimeTotalMetrics) * etlTraceInformation.CPUMetricPerInterval * 100;
                    break;
                }
            }

            List<DiagnosticProcessInfo> processes = new List<DiagnosticProcessInfo>();
            foreach (var process in dataFile.Processes)
            {
                bool isCoreProcess = coreProcesses.Any(x => x.Id == process.ProcessID && x.Name == process.Name);
                var p = new DiagnosticProcessInfo(process, isCoreProcess);
                processes.Add(p);
            }

            using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "traceInfo.json")))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, etlTraceInformation);
            }

            using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, "processCpuInfo.json")))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, processes.OrderByDescending(x => x.CPUMSec)
                    .Where(x => ShouldGenerateCpuStacks(x.CPUMSec))
                    .Where(s => !string.IsNullOrWhiteSpace(s.SiteName) || s.IsCoreProcess)
                    .ToArray());
            }
        }

        private static SymbolReader LoadSymbols(TraceLog dataFile)
        {
            Logger.LogStatus("Loading symbols");
            var symbolReader = SymbolUtilities.GetSymbolReader(m_symbolFilePath, dataFile.FilePath);

            foreach (var process in dataFile.Processes)
            {
                if (process.ProcessID == m_ProcessId)
                {
                    int symbolCount = 0;
                    int totalSymbolCount = process.LoadedModules.Count();
                    foreach (var module in process.LoadedModules)
                    {
                        if (module.ModuleFile.CodeAddressesInModule != 0 && !ExcludedModulesForLoadingSymbols.Any(x => x.ToLower() == module.Name.ToLower()))
                        {
                            dataFile.CodeAddresses.LookupSymbolsForModule(symbolReader, module.ModuleFile);
                        }
                        symbolCount++;
                        if (symbolCount % 50 == 0)
                        {
                            Logger.LogStatus($"Loaded {symbolCount} of {totalSymbolCount} symbols...");
                        }
                    }
                }
            }

            return symbolReader;
        }

        private static void GenerateStackTracesForSlowRequests(SymbolReader symbolReader, TraceLog dataFile, IEnumerable<IisRequest> slowRequests, ClrProfilingAnalyzerStats stats)
        {
            Logger.LogStatus("Extracting stack traces from trace file");
            MutableTraceEventStackSource mutStacks = new MutableTraceEventStackSource(dataFile)
            {
                ShowUnknownAddresses = false
            };

#pragma warning disable 618 
            var computer = new ThreadTimeStackComputer(dataFile, symbolReader);
#pragma warning restore 618 

            computer.ExcludeReadyThread = true;
            computer.GenerateThreadTimeStacks(mutStacks);

            MutableTraceEventStackSource mutStacksAsync = new MutableTraceEventStackSource(dataFile)
            {
                ShowUnknownAddresses = false
            };

#pragma warning disable 618 
            var computerAsync = new ThreadTimeStackComputer(dataFile, symbolReader);
#pragma warning restore 618 

            computerAsync.ExcludeReadyThread = true;
            computerAsync.UseTasks = true;
            computerAsync.GroupByStartStopActivity = true;

            bool shouldParseAsync = true;

            try
            {
                computerAsync.GenerateThreadTimeStacks(mutStacksAsync);
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while generating ASYNC stacks", ex);
                shouldParseAsync = false;
            }

            Logger.LogStatus("Generating stack-traces for slow requests");
            int counterStacksThread = 0;
            int counterStacksActivity = 0;
            int counter = 0;
            foreach (var request in slowRequests)
            {
                counter++;
                IisPipelineEvent slowestPipelineEvent = IisRequestParser.GetSlowestEvent(request);

                FilterParams filterParams = new FilterParams()
                {
                    StartTimeRelativeMSec = slowestPipelineEvent.StartTimeRelativeMSec.ToString(),
                    EndTimeRelativeMSec = slowestPipelineEvent.EndTimeRelativeMSec.ToString(),

                    //Thread(38008); (11276)
                    IncludeRegExs = string.Format("Process% w3wp ({0});Thread ({1})", m_ProcessId, slowestPipelineEvent.StartThreadId)
                };

                double executionTime = slowestPipelineEvent.EndTimeRelativeMSec - slowestPipelineEvent.StartTimeRelativeMSec;

                if ((slowestPipelineEvent.StartThreadId == slowestPipelineEvent.EndThreadId) || (slowestPipelineEvent.EndThreadId == 0))
                {
                    bool hasThreadStack = false;
                    hasThreadStack = GenerateStackTraces(mutStacks, filterParams, request.ContextId.ToString(), true, executionTime);
                    hasThreadStack = GenerateStackTraces(mutStacks, filterParams, request.ContextId.ToString(), false, executionTime);
                    request.HasThreadStack = hasThreadStack;

                    if (request.HasThreadStack)
                    {
                        counterStacksThread++;
                    }
                }

                shouldParseAsync = shouldParseAsync && request.RelatedActivityId != Guid.Empty;

                if (shouldParseAsync)
                {
                    FilterParams filterParamsAsync = new FilterParams()
                    {
                        StartTimeRelativeMSec = slowestPipelineEvent.StartTimeRelativeMSec.ToString(),
                        EndTimeRelativeMSec = slowestPipelineEvent.EndTimeRelativeMSec.ToString(),
                        IncludeRegExs = request.RelatedActivityId.ToString(),
                        FoldRegExs = "ntoskrnl!%ServiceCopyEnd;System.Runtime.CompilerServices.Async%MethodBuilder;^STARTING TASK"
                    };

                    bool hasActivityStack = false;
                    hasActivityStack = GenerateStackTraces(mutStacksAsync, filterParamsAsync, request.ContextId.ToString(), false, executionTime, true);
                    hasActivityStack = GenerateStackTraces(mutStacksAsync, filterParamsAsync, request.ContextId.ToString(), true, executionTime, true);
                    request.HasActivityStack = hasActivityStack;

                    if (request.HasActivityStack)
                    {
                        counterStacksActivity++;
                    }
                }

                Logger.LogInfo(string.Format("Generated Stack Trace for Request {0}. {1}", counter.ToString(), request.Path));

            }

            stats.StackTraceCount = counterStacksThread;
            stats.StackTraceCountAsync = counterStacksActivity;
        }

        private static List<ModuleInfo> GetModuleExcecutionPercent(Dictionary<Guid, IisRequest> iisRequests, double totalTimeInRequestExecution)
        {
            Dictionary<string, double> modulesExecutionTime = new Dictionary<string, double>();

            foreach (var request in iisRequests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderByDescending(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).Take(MAX_SLOWEST_REQUESTS_TO_DISPLAY).Where((m => (m.EndTimeRelativeMSec - m.StartTimeRelativeMSec) > MIN_REQUEST_DURATION_IN_MILLISECONDS)))
            {
                var pipelineNodes = GetPipelineNode(request.PipelineEvents);

                double timeAccountedForThisRequest = 0;

                foreach (var node in pipelineNodes)
                {
                    var pipeLineNode = node.GetCostliestChild(node);

                    if (pipeLineNode.Duration > 0)
                    {

                        if (!modulesExecutionTime.ContainsKey(pipeLineNode.name))
                        {
                            modulesExecutionTime.Add(pipeLineNode.name, pipeLineNode.Duration);
                        }
                        else
                        {
                            modulesExecutionTime[pipeLineNode.name] = modulesExecutionTime[pipeLineNode.name] + pipeLineNode.Duration;
                        }

                        timeAccountedForThisRequest = timeAccountedForThisRequest + pipeLineNode.Duration;
                    }
                }
                double requestDuration = request.EndTimeRelativeMSec - request.StartTimeRelativeMSec;

                if (timeAccountedForThisRequest / requestDuration < 0.5)
                {
                    Logger.LogDiagnoserVerboseEvent($"WARNING:PercentRecorded = {timeAccountedForThisRequest / requestDuration}% - Request {request.Path} with ID {request.ContextId}  took {requestDuration} and we accounted {timeAccountedForThisRequest} ");
                    //TODO :: Need to think what to do here
                }
                else if (timeAccountedForThisRequest > requestDuration)
                {
                    // TODO :: So this can happen if a request has a CHILD REQUEST EVENT
                    // Unfortunately, this is messing our stats as of now
                    Logger.LogDiagnoserVerboseEvent($"WARNING:Accounted More time PercentRecorded = {timeAccountedForThisRequest / requestDuration}% - Request {request.Path}  with ID {request.ContextId}  took {requestDuration} and we accounted {timeAccountedForThisRequest} ");
                    foreach (var node in pipelineNodes)
                    {
                        var pipeLineNode = node.GetCostliestChild(node);
                        if (pipeLineNode.Duration > 10)
                        {
                            Logger.LogInfo($"\t {pipeLineNode.name} took {pipeLineNode.Duration} ms");
                        }
                    }
                }


            }

            var moduleExecutionPercent = (from x in modulesExecutionTime.OrderByDescending(x => x.Value).Take(5)
                                          select new ModuleInfo
                                          {
                                              ModuleName = GetShortNameForModule(x.Key),
                                              Percent = Math.Round((x.Value / totalTimeInRequestExecution) * 100, 2),
                                              TimeSpent = Math.Round(x.Value)
                                          }).ToList();

            return moduleExecutionPercent;
        }

        private static string GetShortNameForModule(string key)
        {
            if (key.Length < 60)
            {
                return key;
            }
            else
            {
                string moduleName = key.Replace("__DynamicModule_", "");
                if (moduleName.Contains(","))
                {
                    var moduleNameArray = moduleName.Split(',');
                    return moduleNameArray[0];
                }
                else
                {
                    var length = (moduleName.Length < 60) ? moduleName.Length : 60;
                    return moduleName.Substring(0, length) + "...";
                }
            }
        }

        private static string GetUrlForTraceFile(string diagSessionPath)
        {
            var sitename = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");

            if (string.IsNullOrWhiteSpace(sitename))
            {
                return "";
            }

            if (sitename.EndsWith(".p.azurewebsites.net"))
            {
                //This site is on ASE
                sitename = sitename.Replace(".p.azurewebsites.net", "");

                //Now we have sitename.asename
                var siteNameArr = sitename.Split('.');

                if (siteNameArr.Length == 2)
                {
                    sitename = siteNameArr[0] + ".scm." + siteNameArr[1] + ".p.azurewebsites.net";
                }
                else
                {
                    return "";
                }

            }
            else
            {
                sitename = sitename.Replace(".azurewebsites.net", ".scm.azurewebsites.net");
            }

            var logsDirectory = Path.Combine(EnvironmentVariables.LocalTemp, "logs");
            if (!logsDirectory.EndsWith("\\"))
            {
                logsDirectory += "\\";
            }
            var url = diagSessionPath.ToLower().Replace(logsDirectory.ToLower(), $"https://{sitename}/api/vfs/Data/Daas/Logs/");
            url = url.Replace('\\', '/');

            return url;
        }

        private static void GetPercentiles(Dictionary<Guid, IisRequest> iisRequests, out int FiftyPercentile, out int NinetyPercentile, out int NinetyFifthPercentile)
        {
            int totalRequestCount = iisRequests.Count();

            var tempOrderedList = iisRequests.Values.Where(x => x.EndTimeRelativeMSec != 0).OrderBy(m => m.EndTimeRelativeMSec - m.StartTimeRelativeMSec).ToList();

            int index = (50 * totalRequestCount) / 100;
            FiftyPercentile = Convert.ToInt32(tempOrderedList.ElementAt(index).EndTimeRelativeMSec - tempOrderedList.ElementAt(index).StartTimeRelativeMSec);

            index = (90 * totalRequestCount) / 100;
            NinetyPercentile = Convert.ToInt32(tempOrderedList.ElementAt(index).EndTimeRelativeMSec - tempOrderedList.ElementAt(index).StartTimeRelativeMSec);

            index = (95 * totalRequestCount) / 100;
            NinetyFifthPercentile = Convert.ToInt32(tempOrderedList.ElementAt(index).EndTimeRelativeMSec - tempOrderedList.ElementAt(index).StartTimeRelativeMSec);
        }

        private static bool GenerateStackTraces(MutableTraceEventStackSource mutStacks, FilterParams filterParams, string contextId, bool justMyCode, double executionTime, bool isAsync = false, bool coreProcess = false)
        {
            if (justMyCode)
            {
                if (coreProcess)
                {
                    filterParams.GroupRegExs = @"\wwwroot\%!->;!=>OTHER";
                }
                else
                {
                    filterParams.GroupRegExs = @"[ASP.NET Just My App] \Temporary ASP.NET Files\->;!dynamicClass.S->;!=>OTHER";
                }
            }
            else
            {
                filterParams.GroupRegExs = @"";
            }

            string stackType = "";
            if (isAsync)
            {
                stackType = "-async";
            }

            FilterStackSource filterSource = new FilterStackSource(filterParams, mutStacks, ScalingPolicyKind.ScaleToData);
            var callTree = new CallTree(ScalingPolicyKind.ScaleToData) { StackSource = filterSource };

            SimpleTreeNode simpleTree = new SimpleTreeNode();

            simpleTree = PopulateCallTree(callTree.Root, simpleTree, isAsync);

            double threashHoldPercent = 0;
            if (executionTime > 0)
            {
                threashHoldPercent = (simpleTree.TimeSpent / executionTime) * 100;
            }

            if (simpleTree.TimeSpent > 0 && threashHoldPercent > 30)
            {
                string filePath = (justMyCode) ? Path.Combine(m_ReportDataPath, contextId + stackType + "-jmc.json") : Path.Combine(m_ReportDataPath, contextId + stackType + ".json");

                using (StreamWriter file = File.CreateText(filePath))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(file, simpleTree);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private static void GenerateCPUStackTraces(TraceLog eventLog, List<AspNetCoreProcess> coreProcesses, bool showUnknownAddresses = false, Predicate<TraceEvent> predicate = null)
        {
            TraceEvents events;
            events = eventLog.Events.Filter((x) => ((predicate == null) || predicate(x)) && x is SampledProfileTraceData && x.ProcessID != 0);

            var traceStackSource = new TraceEventStackSource(events)
            {
                ShowUnknownAddresses = showUnknownAddresses
            };

            // We clone the samples so that we don't have to go back to the ETL file from here on.  
            var copySource = CopyStackSource.Clone(traceStackSource);

            CallTree callTree = new CallTree(ScalingPolicyKind.ScaleToData);
            FilterParams filterParams = new FilterParams();
            var filterStackSource = new FilterStackSource(filterParams, copySource, ScalingPolicyKind.ScaleToData);
            callTree.StackSource = filterStackSource;

            g_CPUMetricPerInterval = (callTree.Root.InclusiveMetric / callTree.Root.DurationMSec) / eventLog.NumberOfProcessors;
            g_CPUTimeTotalMetrics = callTree.Root.InclusiveMetric;



            // Changing to FirstOrDefault in the rarest case if the PID got reused and got assigned to w3wp (Yes it did happen in 1 rare case)    
            var process = eventLog.Processes.Where(x => x.ProcessID == m_ProcessId && x.Name.ToLower() == "w3wp").FirstOrDefault();
            if (process != null && ShouldGenerateCpuStacks(process.CPUMSec))
            {
                GenerateCpuStackForProcess(callTree, copySource, traceStackSource, showUnknownAddresses, process, process.Name, process.ProcessID, predicate);
            }

            foreach (var p in coreProcesses)
            {
                var coreProcess = eventLog.Processes.Where(x => x.ProcessID == p.Id && x.Name.ToLower() == p.Name.ToLower()).FirstOrDefault();
                if (coreProcess != null && ShouldGenerateCpuStacks(coreProcess.CPUMSec))
                {
                    GenerateCpuStackForProcess(callTree, copySource, traceStackSource, showUnknownAddresses, coreProcess, coreProcess.Name, coreProcess.ProcessID, predicate, true);
                }
            }

        }

        private static bool ShouldGenerateCpuStacks(float cPUMSec)
        {
            var thisProcessCpu = cPUMSec / g_CPUTimeTotalMetrics * g_CPUMetricPerInterval * 100;
            return (cPUMSec / g_CPUTimeTotalMetrics * g_CPUMetricPerInterval * 100 > 2);
        }

        private static void GenerateCpuStackForProcess(CallTree callTree, CopyStackSource copySource, TraceEventStackSource traceStackSource, bool showUnknownAddresses, TraceProcess process, string name, int processID, Predicate<TraceEvent> predicate = null, bool coreProcess = false)
        {
            TraceEvents eventsThisProcess = process.EventsInProcess.Filter((x) => ((predicate == null) || predicate(x)) && x is SampledProfileTraceData);
            traceStackSource = new TraceEventStackSource(eventsThisProcess);
            traceStackSource.ShowUnknownAddresses = showUnknownAddresses;

            // We clone the samples so that we don't have to go back to the ETL file from here on.  
            var copySourceThisProcess = CopyStackSource.Clone(traceStackSource);

            FilterParams filterParamsProcess = new FilterParams()
            {
                IncludeRegExs = $"Process% {name} ({process.ProcessID})"
            };

            var filterStackSourceProcess = new FilterStackSource(filterParamsProcess, copySourceThisProcess, ScalingPolicyKind.ScaleToData);
            callTree.StackSource = filterStackSourceProcess;

            // Now filter for the collected process only
            SimpleTreeNode simpleTree = new SimpleTreeNode();

            simpleTree = PopulateCallTree(callTree.Root, simpleTree, false);

            using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, $"cpuStacks-{name}-{processID}.json")))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, simpleTree);
            }

            if (coreProcess)
            {
                filterParamsProcess.GroupRegExs = @"\wwwroot\%!->;!=>OTHER";
            }
            else
            {
                filterParamsProcess.GroupRegExs = @"[ASP.NET Just My App] \Temporary ASP.NET Files\->;!dynamicClass.S->;!=>OTHER";
            }


            filterStackSourceProcess = new FilterStackSource(filterParamsProcess, copySourceThisProcess, ScalingPolicyKind.ScaleToData);
            callTree.StackSource = filterStackSourceProcess;

            simpleTree = PopulateCallTree(callTree.Root, simpleTree, false);

            using (StreamWriter file = File.CreateText(Path.Combine(m_ReportDataPath, $"cpuStacksJmc-{name}-{processID}.json")))
            {
                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(file, simpleTree);
            }
        }

        //
        // fileName will be in this format profile_b4f747_IISProfiling_w3wp_4860
        //
        private static int ExtractProcessIdFromEtlFileName(string fileName)
        {
            int processId = 0;
            int processIdStartIndex = fileName.IndexOf("w3wp_") + 5;
            if (fileName.Length > processIdStartIndex)
            {
                var processIdString = fileName.Substring(processIdStartIndex);
                var underScorePosition = processIdString.IndexOf('_');
                if (underScorePosition > 0)
                {
                    processIdString = processIdString.Substring(0, underScorePosition);
                    int.TryParse(processIdString, out processId);
                }
                else
                {
                    int.TryParse(processIdString, out processId);
                }

            }
            return processId;
        }

        private static string ExtractEtlFromDiagSession(out string unCompressedPath)
        {
            unCompressedPath = string.Empty;
            string etlFilePath = "";

            Logger.LogDiagnoserVerboseEvent($"Opening DiagSessionFile {m_DiagSessionPath}");

            string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string pathToNativePackagingDll = Path.Combine(directoryName, "DiagnosticsHub.Packaging.dll");

            DhPackage dhPackage = DhPackage.Open(m_DiagSessionPath);
            string resourceIdentity = "DiagnosticsHub.Resource.EtlFile";

            ResourceInfo[] resources;
            dhPackage.GetResourceInformationByType(resourceIdentity, out resources);

            IEnumerable<ResourceInfo> orderedResources = resources.OrderBy(r => DhPackagingExtensions.ToDateTime(r.TimeAddedUTC));

            bool failedExtract = false;
            foreach (var resource in resources)
            {
                Guid resourceId = resource.ResourceId;
                string localFilePath = string.Empty;

                try
                {
                    localFilePath = GetLocalFilePath(m_DiagSessionPath, dhPackage, resource, ".etl");
                    Logger.LogDiagnoserVerboseEvent($"Found '{resource.ResourceId} resource '{resource.Name}'. Loading ...");
                    etlFilePath = localFilePath;
                }
                catch (Exception)
                {
                    failedExtract = true;
                    break;
                }
            }

            if (failedExtract)
            {
                string localFilePath = string.Empty;
                try
                {
                    //
                    // Doing this to avoid the PathTooLong exception which happens
                    // while extracing the zip file
                    //

                    if (m_DiagSessionPath.StartsWith(EnvironmentVariables.LocalTemp, StringComparison.OrdinalIgnoreCase))
                    {
                        unCompressedPath = Path.Combine(EnvironmentVariables.LocalTemp, "ETL." + Guid.NewGuid().ToString().Replace("-", ""));
                    }
                    else
                    {
                        unCompressedPath = Path.Combine(Path.GetDirectoryName(m_DiagSessionPath), "UnCompressed");
                    }

                    Logger.LogDiagnoserVerboseEvent($"Uncompressing {m_DiagSessionPath} to {unCompressedPath}...");
                    if (Directory.Exists(unCompressedPath))
                    {
                        DeleteDirectory(unCompressedPath);
                    }

                    ZipFile.ExtractToDirectory(m_DiagSessionPath, unCompressedPath);

                    foreach (var dir in Directory.GetDirectories(unCompressedPath))
                    {
                        if (string.IsNullOrWhiteSpace(localFilePath))
                        {
                            localFilePath = Directory.GetFiles(dir, "*.etl").FirstOrDefault();
                            Logger.LogDiagnoserVerboseEvent($"Found {localFilePath}");
                            etlFilePath = localFilePath;
                        }

                        if (string.IsNullOrWhiteSpace(m_symbolFilePath))
                        {
                            m_symbolFilePath = Directory.GetDirectories(dir, "SymCache").FirstOrDefault();
                        }
                        if (!string.IsNullOrWhiteSpace(localFilePath) && !string.IsNullOrWhiteSpace(m_symbolFilePath))
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogDiagnoserErrorEvent("Failed to Unzip the ETL file", ex);
                }

            }

            return etlFilePath;
        }

        /// <summary>
        /// Depth-first recursive delete, with handling for descendant 
        /// directories open in Windows Explorer.
        /// </summary>
        private static void DeleteDirectory(string path)
        {
            foreach (string directory in Directory.GetDirectories(path))
            {
                DeleteDirectory(directory);
            }

            try
            {
                Directory.Delete(path, true);
            }
            catch (IOException)
            {
                Directory.Delete(path, true);
            }
            catch (UnauthorizedAccessException)
            {
                Directory.Delete(path, true);
            }
        }
        private static SimpleTreeNode PopulateCallTree(CallTreeNode node, SimpleTreeNode simpleNode, bool asyncRequest)
        {
            simpleNode.FunctionName = node.DisplayName;
            simpleNode.TimeSpent = node.InclusiveMetric;
            simpleNode.ExclusiveTime = node.ExclusiveMetric;
            simpleNode.InclusiveMetricPercent = node.InclusiveMetricPercent;
            simpleNode.childNodes = new List<SimpleTreeNode>();

            if (node.Callees != null)
            {
                foreach (var item in node.Callees)
                {
                    if (item.InclusiveMetric > 30 || asyncRequest)
                    {
                        SimpleTreeNode simpleChildNode = new SimpleTreeNode();
                        simpleNode.childNodes.Add(simpleChildNode);
                        PopulateCallTree(item, simpleChildNode, asyncRequest);
                    }
                }
            }
            return simpleNode;
        }

        private static List<PipelineNode> GetPipelineNode(List<IisPipelineEvent> pipeLineEvents, List<IisPipelineEvent> alreadyAdded = null, int level = 0)
        {
            List<PipelineNode> nodes = new List<PipelineNode>();
            if (alreadyAdded == null)
            {
                alreadyAdded = new List<IisPipelineEvent>();
            }

            foreach (var pipeLineEvent in pipeLineEvents)
            {
                if (!alreadyAdded.Contains(pipeLineEvent))
                {
                    var childEvents = pipeLineEvents.Where(x => ((x.StartTimeRelativeMSec > pipeLineEvent.StartTimeRelativeMSec) && (x.EndTimeRelativeMSec <= pipeLineEvent.EndTimeRelativeMSec) && (x.EndTimeRelativeMSec != 0)));
                    PipelineNode node = new PipelineNode(pipeLineEvent.StartTimeRelativeMSec, pipeLineEvent.EndTimeRelativeMSec, pipeLineEvent.Name);

                    if (childEvents.Count() > 0)
                    {
                        node.Children = GetPipelineNode(childEvents.ToList(), alreadyAdded, level + 1);
                        alreadyAdded.AddRange(childEvents);
                    }

                    if (!alreadyAdded.Contains(pipeLineEvent))
                    {
                        nodes.Add(node);
                    }
                }
            }
            return nodes;
        }
    }
}
