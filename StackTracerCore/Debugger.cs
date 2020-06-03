using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;
using Newtonsoft.Json;

namespace StackTracerCore
{
    class StackTracerStats
    {
        public string StatsType;
        public string SiteName;
        public int ThreadCount;
        public double TimeTotal = 0;
        public double TimeInDebuggerTicks = 0;
        public double TimeProcessPaused = 0;
        public double TimeFetchingStackFrames = 0;
        public double TimeAttachDetatch = 0;
        public double TimeFetchingThreads = 0;
    }
    public class Debugger
    {
        static List<string> ExcludedStackFrames = new List<string> { "HelperMethodFrame", "ContextTransitionFrame", "InlinedCallFrame", "DebuggerU2MCatchHandlerFrame" };
        static List<string> ExcludedIfPartOfStackFrames = new List<string> { "DomainNeutralILStubClass.IL_STUB_PInvoke", "DomainNeutralILStubClass.IL_STUB_ReversePInvoke", "DomainNeutralILStubClass.IL_STUB_ReversePInvoke" };
        static List<int> StackTraceHashes = new List<int>();

        const int MAX_THREADS_TO_DUMP = 1000;

        private static void TraceLine(string message)
        {
            Console.WriteLine($"[{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}] {message}");
        }

        public static List<ManagedThread> CollectTraces(int processId)
        {
            DaaS.Logger.Init(string.Empty, string.Empty, "StackTracerCore", true);
            List<ManagedThread> stacks = new List<ManagedThread>();
            StackTracerStats stats = new StackTracerStats();
            stats.StatsType = "StackTracer";
            var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";

            try
            {
                
                if (!string.IsNullOrWhiteSpace(siteName))
                {
                    stats.SiteName = siteName;
                }

                DateTime functionStartTime = DateTime.Now;
                TraceLine("CollectTraces Function started");

                DataTarget target = DataTarget.AttachToProcess(processId, 20000, AttachFlag.NonInvasive);
                TraceLine($"Attached to process {processId}");
                DateTime processAttachtedTime = DateTime.Now;
                stats.TimeAttachDetatch += processAttachtedTime.Subtract(functionStartTime).TotalMilliseconds;

                DefaultSymbolLocator._NT_SYMBOL_PATH = GetSymbolPath();
                TraceLine("sympath = " + DefaultSymbolLocator._NT_SYMBOL_PATH);

                foreach (ClrInfo version in target.ClrVersions)
                {                    
                    TraceLine("Found CLR Version:" + version.Version.ToString());

                    ModuleInfo dacInfo = version.DacInfo;
                    TraceLine(string.Format("Filesize:  {0:X}", dacInfo.FileSize));
                    TraceLine(string.Format("Timestamp: {0:X}", dacInfo.TimeStamp));
                    TraceLine(string.Format("Dac File:  {0}", dacInfo.FileName));

                    string dacLocation = version.DacInfo.FileName;
                    if (!string.IsNullOrEmpty(dacLocation))
                    {
                        TraceLine("Local dac location: " + dacLocation);
                    }

                    ClrRuntime runtime = target.ClrVersions[0].CreateRuntime(version.LocalMatchingDac, true);

                    var timeThreadStart = DateTime.Now;
                    var threads = runtime.Threads.Where(t => t.IsAlive);
                    stats.TimeFetchingThreads = DateTime.Now.Subtract(timeThreadStart).TotalMilliseconds;
                    stats.ThreadCount = threads.Count();

                    foreach (ClrThread thread in threads.Take(MAX_THREADS_TO_DUMP))
                    {
                        if (!thread.IsAlive)
                            continue;

                        ManagedThread t = new ManagedThread
                        {
                            OSThreadId = thread.OSThreadId,
                            GcMode = thread.GcMode,
                            IsBackground = thread.IsBackground,
                            IsFinalizer = thread.IsFinalizer,
                            IsGC = thread.IsGC,
                            IsSuspendingEE = thread.IsSuspendingEE,
                            IsThreadpoolCompletionPort = thread.IsThreadpoolCompletionPort,
                            IsThreadpoolWorker = thread.IsThreadpoolWorker,
                            IsThreadpoolTimer = thread.IsThreadpoolTimer,
                            IsThreadpoolGate = thread.IsThreadpoolGate,
                            LockCount = thread.LockCount,
                            ManagedThreadId = thread.ManagedThreadId
                        };

                        var stackTrace = new List<string>();

                        DateTime dtStackTrace = DateTime.Now;
                        var threadStackTrace = thread.StackTrace;
                        stats.TimeFetchingStackFrames += DateTime.Now.Subtract(dtStackTrace).TotalMilliseconds;

                        foreach (ClrStackFrame frame in threadStackTrace)
                        {
                            DateTime dtTemp = DateTime.Now;
                            var stackframe = frame.DisplayString;
                            if (!ExcludedStackFrames.Any(s => s == stackframe))
                            {
                                if (!ExcludedIfPartOfStackFrames.Any(s => stackframe.Contains(s)))
                                {
                                    stackTrace.Add(stackframe);
                                }
                            }

                            stats.TimeInDebuggerTicks += DateTime.Now.Subtract(dtTemp).TotalMilliseconds;
                        }

                        if (stackTrace.Count > 3)
                        {
                            DateTime dtTemp = DateTime.Now;
                            t.CallStackHash = string.Join(Environment.NewLine, stackTrace).GetHashCode();
                            if (!StackTraceHashes.Contains(t.CallStackHash))
                            {
                                StackTraceHashes.Add(t.CallStackHash);
                                t.CallStack = stackTrace;
                            }
                            stacks.Add(t);
                            stats.TimeInDebuggerTicks += DateTime.Now.Subtract(dtTemp).TotalMilliseconds;
                        }
                    }
                }
                var processDetachtedTime = DateTime.Now;
                target.DebuggerInterface.DetachProcesses();
                stats.TimeAttachDetatch += DateTime.Now.Subtract(processDetachtedTime).TotalMilliseconds;
                stats.TimeProcessPaused = DateTime.Now.Subtract(processAttachtedTime).TotalMilliseconds;
                stats.TimeTotal = DateTime.Now.Subtract(functionStartTime).TotalMilliseconds;
                TraceLine("Process Detatched");

                TraceLine($"Total Time =  { stats.TimeTotal} ms");
                TraceLine($"    TimeFetchingThreads     =  {stats.TimeFetchingThreads} ms");
                TraceLine($"    TimeFetchingStackFrames =  {stats.TimeFetchingStackFrames} ms");
                TraceLine($"    TimeInDebuggerTicks     =  {stats.TimeInDebuggerTicks} ms");
                TraceLine($"    TimeProcessPaused       =  {stats.TimeProcessPaused} ms");
                TraceLine($"    TimeAttachDetatch       =  {stats.TimeAttachDetatch} ms");

                DaaS.Logger.TraceStats(JsonConvert.SerializeObject(stats));
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogDiagnoserErrorEvent("StackTracer: Failed while collecting stacktraces", ex);
            }

            return stacks;
        }

        static string GetSymbolPath()
        {
            string path = @"SRV*D:\home\data\DaaS\symbols*http://msdl.microsoft.com/download/symbols";
            return path;
        }
    }
}
