﻿// -----------------------------------------------------------------------
// <copyright file="Options.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using CommandLine;

namespace DiagLauncher
{
    class Options
    {
        [Option('t', "tool", HelpText = "Specify the diagnostic tool to execute. E.g. MemoryDump, Profiler")]
        public string Tool { get; set; }

        [Option('m', "mode", HelpText = "Specify the diagnoser mode. Allowed options are  [Collect,CollectAndAnalyze,CollectKillAnalyze]")]
        public string Mode { get; set; }

        [Option('p', "params", HelpText = "Specify any additional tool params that diagnostic tool supports")]
        public string ToolParams { get; set; }

        [Option('l', "listdiagnosers", Required = false, HelpText = "List all available diagnosers")]
        public bool ListDiagnosers { get; set; }
    }
}
