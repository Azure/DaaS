// -----------------------------------------------------------------------
// <copyright file="IMonitoringRule.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS
{
    public interface ICpuMonitoringRule
    {
        void LogStartup(Action<string, bool> appendToMonitoringLog);
        bool TakeActionOnHighCpu(int processId, string processName, DateTime monitoringStartTime, Action<string, bool> appendToMonitoringLog);
        bool ShouldTerminateRule(Action<string, bool> appendToMonitoringLog);
        bool ShouldAnalyze();

        bool MonitorScmProcesses { get; }
        int MonitorDuration { get; }
        int CpuThreshold { get; }
        int ThresholdSeconds { get; }
        string SessionId { get; }
    }
}
