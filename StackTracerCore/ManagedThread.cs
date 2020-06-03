using System;
using System.Collections.Generic;
using Microsoft.Diagnostics.Runtime;

namespace StackTracerCore
{
    public class ManagedThread
    {
        public uint OSThreadId;
        public GcMode GcMode;
        public bool IsBackground;
        public bool IsFinalizer;
        public bool IsGC;
        public bool IsSuspendingEE;
        public bool IsThreadpoolCompletionPort;
        public bool IsThreadpoolWorker;
        public bool IsThreadpoolTimer;
        public bool IsThreadpoolGate;
        public uint LockCount;
        public int ManagedThreadId;
        public List<string> CallStack;
        public int CallStackHash;        
    }
}
