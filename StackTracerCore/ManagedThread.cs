//-----------------------------------------------------------------------
// <copyright file="ManagedThread.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

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
