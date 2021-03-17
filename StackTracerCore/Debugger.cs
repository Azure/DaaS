//-----------------------------------------------------------------------
// <copyright file="Debugger.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DaaS;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

namespace StackTracerCore
{
    public class Debugger
    {
        static readonly List<int> StackTraceHashes = new List<int>();
        static readonly string SiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";

        const int MaxThreadsToDump = 1000;

        private static void TraceLine(string message, bool generateBackendEvent = true)
        {
            Console.WriteLine($"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}");
            if (generateBackendEvent)
            {
                Logger.LogDiagnoserVerboseEvent(message);
            }
        }

        private static void TraceError(string message, Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message} {ex}");
            Logger.LogDiagnoserErrorEvent(message, ex);
        }

        private static void TraceWarning(string message, Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message} {ex}");
            Logger.LogDiagnoserWarningEvent(message, ex);
        }

        public static List<ManagedThread> CollectTraces(int processId, string outputDirectory)
        {
            Logger.Init(string.Empty, outputDirectory, "StackTracerCore", true);
            List<ManagedThread> stacks = new List<ManagedThread>();
            StackTracerStats stats = new StackTracerStats
            {
                StatsType = "StackTracer",
                SiteName = SiteName
            };

            try
            {
                DateTime startTime = DateTime.UtcNow;
                TraceLine("CollectTraces Function started");

                DataTarget target = null;
                DateTime processAttachedTime = DateTime.MinValue;

                try
                {
                    target = DataTarget.AttachToProcess(processId, suspend: true);
                    TraceLine($"Attached to process {processId}");
                    processAttachedTime = DateTime.UtcNow;
                    stats.TimeProcessAttached = DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;

                    foreach (ClrInfo version in target.ClrVersions)
                    {
                        TraceLine("Found CLR Version:" + version.Version.ToString());

                        DacInfo dacInfo = version.DacInfo;
                        TraceLine(string.Format("Dac File:  {0}", dacInfo.PlatformSpecificFileName));

                        string dacLocation = version.DacInfo.LocalDacPath;
                        if (!string.IsNullOrEmpty(dacLocation))
                        {
                            TraceLine("Local dac location: " + dacLocation);
                        }

                        ClrRuntime runtime = null;

                        try
                        {
                            runtime = version.CreateRuntime(dacLocation, true);
                        }
                        catch (Exception ex)
                        {
                            TraceWarning($"StackTracer: Failed to create runtime", ex);
                        }

                        if (runtime == null)
                        {
                            continue;
                        }

                        TraceLine($"ClrRuntime object created successfully for {version.Version}");

                        var timeThreadStart = DateTime.UtcNow;
                        var threads = runtime.Threads.Where(t => t.IsAlive);
                        stats.TimeFetchingThreads = DateTime.UtcNow.Subtract(timeThreadStart).TotalMilliseconds;
                        stats.ThreadCount = threads.Count();

                        foreach (ClrThread thread in threads.Take(MaxThreadsToDump))
                        {
                            ManagedThread t = new ManagedThread
                            {
                                OSThreadId = thread.OSThreadId,
                                GcMode = thread.GCMode,
                                IsBackground = thread.IsBackground,
                                IsFinalizer = thread.IsFinalizer,
                                IsGCSuspendPending = thread.IsGCSuspendPending,
                                LockCount = thread.LockCount,
                                ManagedThreadId = thread.ManagedThreadId,
                                IsUserSuspended = thread.IsUserSuspended
                            };

                            var stackTrace = new List<string>();

                            DateTime dtStackTrace = DateTime.UtcNow;
                            var threadStackTrace = thread.EnumerateStackTrace();
                            stats.TimeFetchingStackFrames += DateTime.UtcNow.Subtract(dtStackTrace).TotalMilliseconds;

                            foreach (ClrStackFrame frame in threadStackTrace)
                            {
                                if(frame == null)
                                {
                                    continue;
                                }

                                if (frame.Kind == ClrStackFrameKind.ManagedMethod)
                                {
                                    var clrMethod = frame.Method;
                                    if (clrMethod != null && clrMethod.Signature != null)
                                    {
                                        stackTrace.Add(clrMethod.Signature);
                                    }
                                }
                                else
                                {
                                    var stackFrame = frame.FrameName;
                                    if (!string.IsNullOrEmpty(stackFrame))
                                    {
                                        stackTrace.Add(stackFrame);
                                    }
                                }
                            }

                            if (stackTrace.Count > 3)
                            {
                                DateTime dtTemp = DateTime.UtcNow;
                                t.CallStackHash = string.Join(Environment.NewLine, stackTrace).GetHashCode();
                                if (!StackTraceHashes.Contains(t.CallStackHash))
                                {
                                    StackTraceHashes.Add(t.CallStackHash);
                                    t.CallStack = stackTrace;
                                }
                                stacks.Add(t);
                            }
                        }

                        try
                        {
                            runtime.Dispose();
                        }
                        catch (Exception ex)
                        {
                            TraceWarning("Encountered error while disposing ClrRuntime", ex);
                        }

                        TraceLine($"Found {stacks.Count()} stacks in process");
                    }
                }
                catch (Exception ex)
                {
                    TraceError("StackTracer: Failed while collecting stacktraces", ex);
                }

                if (target != null)
                {
                    //
                    // Calling Dispose is super critical as that detatches
                    // the process else the process may be left suspended
                    //

                    try
                    {
                        target.Dispose();
                    }
                    catch (Exception ex)
                    {
                        TraceError("Encountered error while disposing DataTarget", ex);
                    }
                }

                var processDetachtedTime = DateTime.UtcNow;
                stats.TimeProcessPaused = DateTime.UtcNow.Subtract(processAttachedTime).TotalMilliseconds;
                stats.TimeTotal = DateTime.UtcNow.Subtract(startTime).TotalMilliseconds;
                TraceLine("Process Detatched");

                TraceLine($"Total Time =  { stats.TimeTotal} ms", generateBackendEvent: false);
                TraceLine($"    TimeFetchingThreads     =  {stats.TimeFetchingThreads} ms", generateBackendEvent: false);
                TraceLine($"    TimeFetchingStackFrames =  {stats.TimeFetchingStackFrames} ms", generateBackendEvent: false);
                TraceLine($"    TimeProcessPaused       =  {stats.TimeProcessPaused} ms", generateBackendEvent: false);
                TraceLine($"    TimeProcessAttached     =  {stats.TimeProcessAttached} ms", generateBackendEvent: false);

                Logger.TraceStats(JsonConvert.SerializeObject(stats));
            }
            catch (Exception ex)
            {
                TraceError("StackTracer: Failed in CollectStackTraces method", ex);
            }

            return stacks;
        }
    }
}
