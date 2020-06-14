//-----------------------------------------------------------------------
// <copyright file="ClrProfilingCollectorStats.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace ClrProflingCollector
{
    class ClrProfilingCollectorStats
    {
        public string StatsType;
        public string InstanceName;
        public string SiteName;
        public int ProcessId;
        public double TraceDurationInSeconds;
        public double TimeToStartTraceInSeconds;
        public double TimeToStopTraceInSeconds;
        public double TimeToGenerateRawStackTraces;
        public string TraceFileName;
        public long TraceFileSizeInMb;
        public Guid ActivityId;
        public string DotNetCoreProcess;
    }
}
