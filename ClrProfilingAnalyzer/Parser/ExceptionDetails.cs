//-----------------------------------------------------------------------
// <copyright file="ExceptionDetails.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace ClrProfilingAnalyzer.Parser
{
    public class ExceptionDetails
    {
        public string ExceptionType;
        public string ExceptionMessage;
        public int ThreadId;
        public int ProcessId;
        public string ProcessName;
        public double TimeStampRelativeMSec;
        public List<string> StackTrace;
        public int StackTraceHash;
    }
}
