// -----------------------------------------------------------------------
// <copyright file="DaasDirectory.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;

namespace DaaS.V2
{
    internal class DaasDirectory
    {
        private const string LogsFolder = "Logs";
        private const string ReportsFolder = "Reports";
        private const string DaasRelativePath = @"/Data/DaaS";

        protected static readonly string daasPath = Path.Combine(Environment.ExpandEnvironmentVariables(@"%HOME%"), "Data", "DaaS");

        internal static string DaasPath { get; } = daasPath;
        internal static string ConfigDir { get; } = Path.Combine(daasPath, "Configuration");
        
        internal static string LogsDir { get; } = Path.Combine(daasPath, LogsFolder);
        internal static string LogsDirRelativePath { get; } = Path.Combine(DaasRelativePath, LogsFolder);
        internal static string LogsTempDir { get; } = Path.Combine(Path.GetTempPath(), LogsFolder);

        internal static string ReportsDir { get; } = Path.Combine(daasPath, ReportsFolder);
        internal static string ReportsDirRelativePath { get; } = Path.Combine(DaasRelativePath, ReportsFolder);
        internal static string ReportsTempDir { get; } = Path.Combine(Path.GetTempPath(), ReportsFolder);
    }
}
