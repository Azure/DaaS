//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DaaS.ApplicationInfo;
using Newtonsoft.Json;
using DaaS;

namespace ClrProflingCollector
{
    partial class Program
    {
        static void Main(string[] args)
        {
            int processId = 0;
            string daasOutputFilePath = args[0];

            bool Is64bit = false;
            bool collectRawStacks = args.Contains("collectRawStacks") ? true : false;
            bool cpuStacksOnly = args.Contains("cpuStacksOnly") ? true : false;
            if (cpuStacksOnly)
            {
                collectRawStacks = false;
            }
            Logger.Init("", daasOutputFilePath, "ClrProfilingCollector", true);

            ClrProfilingCollectorStats stats = new ClrProfilingCollectorStats
            {
                StatsType = "ClrProfilingCollector"
            };

            bool failedToIdentifyProcessToTrace = false;
            var childProcesses = new List<int>();
            var childProcessNames = new List<string>();

            try
            {
                AppModelDetector detector = new AppModelDetector();
                var webconfigDirecotryPath = new DirectoryInfo(EnvironmentVariables.WebConfigDirectoryPath);
                if (webconfigDirecotryPath.Exists)
                {
                    var version = detector.Detect(webconfigDirecotryPath);
                    if (!string.IsNullOrWhiteSpace(version.CoreProcessName))
                    {
                        Logger.LogDiagnoserVerboseEvent($".Net Core detected - need to trace {version.CoreProcessName} as well");
                        childProcessNames.Add(version.CoreProcessName);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while reading configuration to check for .net core processes", ex);
            }

            foreach (var process in Process.GetProcesses())
            {
                if (process.ProcessName == "w3wp" && processId == 0)
                {
                    try
                    {
                        var envVar = DaaS.Utilities.GetEnvironmentVariablesCore(process.Handle);
                        if (!DaaS.Utilities.GetIsScmSite(envVar))
                        {
                            processId = process.Id;
                            stats.ProcessId = processId;
                            stats.InstanceName = Environment.MachineName;
                            stats.SiteName = Logger.SiteName;
                            stats.ActivityId = Logger.ActivityId;

                            if (DaaS.Utilities.GetProcessBitness(process.Handle) == 64)
                            {
                                Is64bit = true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDiagnoserErrorEvent("Finding right process failed", ex);
                        failedToIdentifyProcessToTrace = true;
                    }

                }
                else if (!string.IsNullOrWhiteSpace(process.ProcessName) && childProcessNames.Contains(process.ProcessName.ToLower()))
                {
                    if (!childProcesses.Contains(process.Id) && process.Id != 0)
                    {
                        stats.DotNetCoreProcess = process.ProcessName;
                        Logger.LogDiagnoserVerboseEvent($"Added {process.Id} for {process.ProcessName} to the list of child processes to trace");
                        childProcesses.Add(process.Id);
                    }
                }
            }

            if (processId == 0)
            {
                if (failedToIdentifyProcessToTrace == true)
                {
                    Logger.TraceFatal("Failed to identify the process to trace. Please check the diaglog file for more information.");
                }
                else
                {
                    Logger.TraceFatal("w3wp.exe for the Web App is not running. Before collecting a profiler trace, make sure that the worker process is started by making a request.");
                }
                return;
            }

            Logger.LogDiagnoserVerboseEvent($"Process to Trace is {processId.ToString()} on instance { Environment.MachineName } ");

            var sleepDuration = ProfileManager.GetIisProfilingDuration().TotalSeconds;
            stats.TraceDurationInSeconds = sleepDuration;

            var startProfilingTime = DateTime.Now;

            Logger.LogStatus("Starting profiler session");
            var result = ProfileManager.StartProfile(processId, !cpuStacksOnly, childProcesses.ToArray());

            if (result.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Logger.TraceFatal($"Failed to profile process {processId.ToString()}. Profiling call failed with {result.StatusCode }", false);
                Logger.LogDiagnoserErrorEvent($"Failed to start profiling for the process", $"Profiling call failed with {result.StatusCode } and message {result.Message}");
                return;
            }
            else
            {
                Logger.LogDiagnoserEvent($"CLRProfilingSession Started with duration {sleepDuration}s");
            }

            var timeToStartProfile = DateTime.Now.Subtract(startProfilingTime).TotalSeconds;
            stats.TimeToStartTraceInSeconds = timeToStartProfile;

            Logger.LogInfo(string.Format("Started sleeping for {0} seconds", sleepDuration));

            Logger.LogStatus($"Profiler session started. Profiler will stop automatically after {sleepDuration} seconds. At this point, please reproduce the problem or browse to your WebApp to ensure that requests get captured in the trace");
            if (sleepDuration > 0)
            {
                Thread.Sleep((int)sleepDuration * 1000);
                Logger.LogDiagnoserEvent("Profiler done sleeping");
            }

            var stopProfilingTime = DateTime.Now;
            Logger.LogStatus("Stopping profiler session");

            bool stopRetried = false;

        stopProfilingLabel:
            try
            {
                result = ProfileManager.StopProfile(processId);
                if (result.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    if (!stopRetried)
                    {
                        stopRetried = true;
                        Logger.LogDiagnoserErrorEvent($"Failed to stop profiling for the process, going to retry once", $"Profiling call failed with {result.StatusCode } and message {result.Message}");
                        Thread.Sleep(5000);
                        goto stopProfilingLabel;

                    }
                    else
                    {
                        Logger.LogDiagnoserErrorEvent($"Failed to stop profiling for the process", $"Profiling call failed with {result.StatusCode } and message {result.Message}");
                        Logger.TraceFatal($"Failed to stop profiling for process { processId.ToString() }. Profiling call failed with { result.StatusCode }", false);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.TraceFatal($"Failed while stopping profiler with exception - {ex.Message}", false);
                Logger.LogDiagnoserErrorEvent("Failed while stopping profiler", ex);
            }

            Logger.LogStatus($"Profiler session stopped");

            var timeToStopProfile = DateTime.Now.Subtract(stopProfilingTime).TotalSeconds;
            stats.TimeToStopTraceInSeconds = timeToStopProfile;

            string filePath = result.FilePath;
            stats.TraceFileName = filePath;
            stats.TraceFileSizeInMb = GetFileSize(filePath);
            try
            {
                if (File.Exists(filePath))
                {
                    string zipFolderPath = CreateZipFolder(filePath);

                    //
                    // Disabling StackTracer temporarily till we upgrade
                    // to CLRMD 2.0 and handle all exceptions 
                    //

                    collectRawStacks = false;

                    if (collectRawStacks)
                    {
                        Logger.LogStatus("Collecting raw stack-traces");
                        var timeToCollectStackTraces = DateTime.Now;
                        CollectStackTracerLog(zipFolderPath, processId, Is64bit);
                        stats.TimeToGenerateRawStackTraces = DateTime.Now.Subtract(timeToCollectStackTraces).TotalSeconds;
                        Logger.LogInfo($"Took [{stats.TimeToGenerateRawStackTraces} s] to generate stacktraces.");
                        Logger.LogStatus("Collected raw stack-traces");
                    }

                    var zipFile = AddCollectedDataToZip(zipFolderPath, filePath);

                    // Copy the file to DAAS output folders
                    var daasOutputFolder = Path.Combine(daasOutputFilePath, Path.GetFileName(zipFile));
                    File.Copy(zipFile, daasOutputFolder, true);
                    Logger.LogDiagnoserVerboseEvent($"Copied temporary file [{zipFile}] to DAAS Folders [{daasOutputFolder}]");

                    try
                    {
                        // Delete the file generated by the Profiler as we already copied that to the DAAS folders
                        File.Delete(zipFile);
                        Logger.LogInfo($"Deleted [{zipFile}]");
                        foreach (var file in Directory.GetFiles(zipFolderPath))
                        {
                            try
                            {
                                File.Delete(file);
                                Logger.LogDiagnoserVerboseEvent($"Deleted {file}");
                            }
                            catch (Exception)
                            {
                            }
                        }
                        Directory.Delete(zipFolderPath);
                        Logger.LogDiagnoserVerboseEvent($"Deleted folder {zipFolderPath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogDiagnoserErrorEvent($"Failed to cleanup temporary zip files and folders", ex);
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.TraceFatal($"Failed while copying the trace file - {filePath} \n {ex.Message} \n { ex.StackTrace} ");
            }

            Logger.TraceStats(JsonConvert.SerializeObject(stats));
            Logger.LogDiagnoserEvent($"CLRProfilingSession completed");
        }

        static string CreateZipFolder(string filePath)
        {
            string folderName = Path.GetDirectoryName(filePath);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

            string zipFolderPath = Path.Combine(folderName, fileNameWithoutExtension);

            if (!Directory.Exists(zipFolderPath))
            {
                Directory.CreateDirectory(zipFolderPath);
            }

            foreach (var item in Directory.GetFiles(zipFolderPath))
            {
                try
                {
                    File.Delete(item);
                }
                catch (Exception)
                {
                }
            }

            return zipFolderPath;
        }

        static string AddCollectedDataToZip(string zipFolderPath, string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string folderName = Path.GetDirectoryName(filePath);
            string zipFileName = Path.GetFileNameWithoutExtension(filePath) + ".zip";

            string zipFile = Path.Combine(folderName, zipFileName);
            string newDiagSessionLocation = Path.Combine(zipFolderPath, fileName);

            Logger.LogInfo($"Moving file from {filePath} to {newDiagSessionLocation}");
            File.Copy(filePath, newDiagSessionLocation, true);

            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent($"Failed while deleting file in AddCollectedDataToZip", ex);
            }

            Logger.LogInfo($"Ziping folder {zipFolderPath} to {zipFile}");

            if (File.Exists(zipFile))
            {
                try
                {
                    File.Delete(zipFile);
                }
                catch (Exception)
                {
                    // do nothing
                }
            }

            ZipFile.CreateFromDirectory(zipFolderPath, zipFile);

            return zipFile;
        }
        private static void CollectStackTracerLog(string zipFolderPath, int processId, bool is64bit)
        {
            try
            {
                var assemblyDir = System.Reflection.Assembly.GetEntryAssembly().Location;
                string clrprofilerPath = Path.GetDirectoryName(assemblyDir);
                string stackTracerPath = Path.Combine(clrprofilerPath, "stacktracer");

                if (is64bit)
                {
                    stackTracerPath = Path.Combine(stackTracerPath, "stacktracer64.exe");
                }
                else
                {
                    stackTracerPath = Path.Combine(stackTracerPath, "stacktracer32.exe");
                }

                MemoryStream outputStream = null;
                MemoryStream errorStream = null;

                var cancellationTokenSource = new CancellationTokenSource();

                Process process = new Process();

                ProcessStartInfo pinfo = new ProcessStartInfo
                {
                    Arguments = $"{processId} {zipFolderPath}",
                    FileName = stackTracerPath
                };

                Logger.LogDiagnoserEvent($"Starting process:{stackTracerPath} with arguments: {pinfo.Arguments}");

                process.StartInfo = pinfo;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                outputStream = new MemoryStream();
                errorStream = new MemoryStream();

                process.Start();

                var tasks = new List<Task>
                {
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardOutput.BaseStream, outputStream, cancellationTokenSource.Token),
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardError.BaseStream, errorStream, cancellationTokenSource.Token)
                };
                process.WaitForExit();

                Task.WhenAll(tasks);

                string output = outputStream.ReadToEnd();
                string error = errorStream.ReadToEnd();

                Logger.LogInfo(output);

                if (!string.IsNullOrEmpty(error))
                {
                    Logger.LogInfo(error);
                }

                if (process.ExitCode != 0)
                {
                    Logger.LogDiagnoserErrorEvent(string.Format(CultureInfo.InvariantCulture, "Starting process {0} failed with the following error code '{1}'.", stackTracerPath, process.ExitCode), $"process.ExitCode = {process.ExitCode.ToString()}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogDiagnoserErrorEvent("Failed while trying to generate raw stack traces using StackTracer", ex);
            }
        }

        private static long GetFileSize(string filePath)
        {
            long fileSize = 0;

            try
            {
                FileInfo info = new FileInfo(filePath);
                fileSize = (info.Length) / (1024 * 1024);
            }
            catch (Exception)
            {
                //no-op
            }
            return fileSize;
        }
    }
}
