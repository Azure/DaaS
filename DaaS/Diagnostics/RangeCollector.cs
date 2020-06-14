//-----------------------------------------------------------------------
// <copyright file="RangeCollector.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Leases;
using DaaS.Storage;

namespace DaaS.Diagnostics
{
    class RangeCollector : Collector
    {

        protected override async Task<bool> RunCollectorCommandAsync(DateTime utcStartTime, DateTime utcEndTime, string outputDir, string sessionId, Lease lease, CancellationToken ct)
        {
            if (DateTime.UtcNow < utcEndTime)
            {
                // Don't bother running range collectors until the time range to collect has elapsed
                var timeToWait = utcEndTime - DateTime.UtcNow;
                Logger.LogDiagnostic("Sleeping for {0}, until it's time to run the {1} collector", timeToWait, Name);
                await Task.Delay(timeToWait);
            }

            var args = ExpandVariablesInArgument(utcStartTime, utcEndTime, outputDir);

            await RunProcessWhileKeepingLeaseAliveAsync(lease, Command, args, sessionId, ct);
            return true;
        }
    }
}
