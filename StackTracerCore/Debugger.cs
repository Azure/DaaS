//-----------------------------------------------------------------------
// <copyright file="Debugger.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using DaaS;
using Microsoft.Diagnostics.Runtime;
using Newtonsoft.Json;

namespace StackTracerCore
{
    public class Debugger
    {
        static readonly HashSet<int> StackTraceHashes = new HashSet<int>();
        static readonly string SiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";

        const int MaxThreadsToDump = 1000;
        const int MaxStackFramesPerThread = 128;

        private static void TraceLine(string message, bool logToKusto = true)
        {
            Console.WriteLine($"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}");
            if (logToKusto)
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
            var stacks = new List<ManagedThread>();
            StackTracerStats stats = new StackTracerStats
            {
                StatsType = "StackTracer",
                SiteName = SiteName
            };

            Stopwatch sw = new Stopwatch();
            sw.Start();

            TraceLine("CollectTraces Function started");
            DataTarget dt = CreateDataTarget(processId);
            stats.TimeProcessAttached = sw.ElapsedMilliseconds;
            sw.Restart();

            try
            {
                foreach (ClrInfo clrInfo in dt?.ClrVersions)
                {
                    int excludedThreads = 0;
                    TraceLine($"Found CLR Version: {clrInfo.Version} DacFile: {clrInfo.DacInfo.PlatformSpecificFileName}, Path: {clrInfo.DacInfo.LocalDacPath}");

                    try
                    {
                        using (ClrRuntime runtime = CreateRuntime(clrInfo, clrInfo.DacInfo.LocalDacPath))
                        {
                            if (runtime is null)
                                continue;

                            var threads = runtime.Threads.Where(t => t.IsAlive);
                            int threadCount = threads.Count();
                            TraceLine($"Alive Thread Count = {threadCount}", logToKusto: false);
                            stats.ThreadCount = threadCount;

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

                                int frameCount = 0;
                                foreach (ClrStackFrame frame in thread.EnumerateStackTrace().Take(MaxStackFramesPerThread))
                                {
                                    frameCount++;

                                    if (frame == null)
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

                                TraceLine($"{thread.OSThreadId} - EnumerateStackTrace returned = {frameCount} frames", false);

                                if (stackTrace.Count > 3)
                                {
                                    t.CallStackHash = stackTrace.Select(frame => frame.GetHashCode())
                                        .Aggregate(0, (accumulator, x) => ((int)(((uint)accumulator << 1) | ((uint)accumulator >> 31)) ^ x));

                                    if (StackTraceHashes.Add(t.CallStackHash))
                                    {
                                        t.CallStack = stackTrace;
                                    }
                                    stacks.Add(t);
                                }
                                else
                                {
                                    excludedThreads++;
                                }
                            }
                            stats.ExcludedThreadCount = excludedThreads;
                        }
                    }
                    catch (Exception e)
                    {
                        // catch block for processing a single ClrRuntime, log that we failed to process a particular version
                        TraceWarning($"StackTracer: Failed to process runtime", e);
                    }

                }

            }
            catch (Exception e)
            {
                // We don't expect an exception here, but just be double sure and log anything that escapes
                TraceError("StackTracer: Failed while collecting stacktraces", e);
            }
            finally
            {
                try
                {
                    //
                    // Calling Dispose is super critical as that detatches
                    // the process else the process may be left suspended
                    //

                    dt?.Dispose();
                    TraceLine("Process detatched successfully");
                }
                catch (Exception ex)
                {
                    TraceError("Encountered error while disposing ClrRuntime", ex);
                }
                finally
                {
                    stats.TimeProcessPaused = sw.ElapsedMilliseconds;
                }
            }

            TraceLine($"Total Time =  { stats.TimeProcessPaused + stats.TimeProcessAttached} ms", logToKusto: false);
            TraceLine($"    TimeProcessPaused       =  {stats.TimeProcessPaused} ms", logToKusto: false);
            TraceLine($"    TimeProcessAttached     =  {stats.TimeProcessAttached} ms", logToKusto: false);
            TraceLine($"    ThreadCount             =  {stats.ThreadCount}", logToKusto: false);
            TraceLine($"    ExcludedThreadCount     =  {stats.ExcludedThreadCount}", logToKusto: false);

            Logger.TraceStats(JsonConvert.SerializeObject(stats));
            return stacks;
        }

        public static ClrRuntime CreateRuntime(ClrInfo version, string dacLocation)
        {
            try
            {
                return version.CreateRuntime(dacLocation, true);
            }
            catch (Exception e)
            {
                TraceWarning($"StackTracer: Failed to create runtime", e);
            }

            return null;
        }

        public static DataTarget CreateDataTarget(int processId)
        {
            try
            {
                return DataTarget.CreateSnapshotAndAttach(processId);
            }
            catch (Exception e)
            {
                TraceError($"StackTracer: Failed to Create DataTarget", e);
            }

            return null;
        }
    }
}
