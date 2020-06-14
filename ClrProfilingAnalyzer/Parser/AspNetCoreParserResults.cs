//-----------------------------------------------------------------------
// <copyright file="AspNetCoreParserResults.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using ClrProfilingAnalyzer.Parser;

namespace ClrProfilingAnalyzer
{
    public class AspNetCoreParserResults
    {
        public Dictionary<string, AspNetCoreRequest> Requests { get; set; }
        public Dictionary<AspNetCoreRequestId, List<AspNetCoreTraceEvent>> AspNetCoreRequestsFullTrace { get; set; }
        public List<AspNetCoreProcess> Processes { get; set; }
        public Dictionary<AspNetCoreRequest, List<AspNetCoreTraceEvent>> FailedRequests { get; set; }
    }
}
