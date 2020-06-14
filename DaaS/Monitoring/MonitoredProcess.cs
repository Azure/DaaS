//-----------------------------------------------------------------------
// <copyright file="MonitoredProcess.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace DaaS
{
    public class MonitoredProcess
    {
        public TimeSpan CPUTimeStart;
        public TimeSpan CPUTimeCurrent;
        public DateTime LastMonitorTime;
        public int ThresholdExeededCount;
        public string Name;
    }
}
