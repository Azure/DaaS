// -----------------------------------------------------------------------
// <copyright file="CrashMonitoringFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS
{
    public class CrashMonitoringFile
    {
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public DateTime Created { get; set; }
        public string ExitCode { get; set; } = string.Empty;
        public Uri Uri { get; set; }

        public CrashMonitoringFile(string fileName, string relativePath, Uri uri, DateTime created)
        {
            FileName = fileName;
            RelativePath = relativePath;
            Created = created;
            Uri = uri;

            var fileArray = FileName.Split('_');
            var exitCodeIndex = fileArray.Length - 3;
            if (exitCodeIndex > 0)
            {
                ExitCode = fileArray[exitCodeIndex];
            }

        }
    }
}