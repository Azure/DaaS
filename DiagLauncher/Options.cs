// -----------------------------------------------------------------------
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

        [Option('s', "sessionId", HelpText = "SessionId to use for the session. Only used when same session being submitted on multiple instances", Hidden = true)]
        public string SessionId { get; internal set; }

        [Option('d', "listdiagnosers", Required = false, HelpText = "List all available diagnosers")]
        public bool ListDiagnosers { get; set; }

        [Option('l', "listsessions", Required = false, HelpText = "List all sessions")]
        public bool ListSessions { get; set; }

        [Option('r', "remove", Required = false, HelpText = "Remove an existing Session")]
        public string SessionIdForDeletion { get; set; }
    }
}
