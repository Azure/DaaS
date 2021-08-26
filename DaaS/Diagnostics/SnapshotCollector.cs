// -----------------------------------------------------------------------
// <copyright file="SnapshotCollector.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Leases;
using DaaS.Storage;

namespace DaaS.Diagnostics
{
    class SnapshotCollector : Collector
    {
        protected override async Task<bool> RunCollectorCommandAsync(DateTime utcStartTime, DateTime utcEndTime, string outputDir,string sessionId, Lease lease, CancellationToken ct)
        {
            var args = ExpandVariablesInArgument(utcStartTime, utcEndTime, outputDir);

            //
            // Run the collector once at the beginning of the timespan and that's it
            //
            await RunProcessWhileKeepingLeaseAliveAsync(lease, Command, args, sessionId, ct);

            return true;
        }

    }
}
