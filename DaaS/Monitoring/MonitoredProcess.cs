// -----------------------------------------------------------------------
// <copyright file="MonitoredProcess.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS
{
    internal class MonitoredProcess
    {
        public TimeSpan CpuTimeStart { get; set; }
        public TimeSpan CpuTimeCurrent { get; set; }
        public DateTime LastMonitorTime { get; set; }
        public DateTime ProcessStartTime { get; set; }
        public int ThresholdExceededCount { get; set; }
        public string Name { get; set; }
    }
}
