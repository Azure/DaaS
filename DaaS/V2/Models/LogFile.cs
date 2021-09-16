// -----------------------------------------------------------------------
// <copyright file="LogFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace DaaS.V2
{
    public class LogFile
    {
        public DateTime StartTime { get; set; }
        public string Name { get; set; }
        public string PartialPath { get; set; }
        public long Size { get; set; }
        public List<Report> Reports { get; set; } = new List<Report>();
        public string TempPath { get; set; }
        public string RelativePath { get; set; }

        internal string GetReportTempPath(string sessionId)
        {
            string path = Path.Combine(
                        DaasDirectory.ReportsTempDir,
                        sessionId,
                        StartTime.ToString(Constants.SessionFileNameFormat));

            FileSystemHelpers.EnsureDirectory(path);
            return path;
        }
    }
}
