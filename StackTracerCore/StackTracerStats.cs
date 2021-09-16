// -----------------------------------------------------------------------
// <copyright file="StackTracerStats.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace StackTracerCore
{
    class StackTracerStats
    {
        public string StatsType;
        public string SiteName;
        public int ThreadCount;
        public int ExcludedThreadCount;
        public double TimeProcessPaused = 0;
        public double TimeProcessAttached = 0;
    }
}
