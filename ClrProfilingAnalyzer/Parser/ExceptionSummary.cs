// -----------------------------------------------------------------------
// <copyright file="ExceptionSummary.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClrProfilingAnalyzer.Parser
{
    public class ExceptionSummary
    {
        public string ProcessName;
        public string ExceptionType;
        public string ExceptionMessage;
        public List<string> StackTrace;
        public int StackTraceHash;
        public int Count;
    }

    public class ExceptionSummaryByName
    {
        public string ProcessName;
        public string ExceptionType;
        public string ExceptionMessage;
        [JsonIgnore]
        public List<int> StackTraceHashes;
        public List<StackSummary> StackTrace;
        public int Count;
    }

    public class StackSummary
    {
        public List<string> StackTrace;
        public int StackTraceHash;
    }

}
