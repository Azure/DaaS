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
        public string Name { get; set; }
        public DateTimeOffset? CreatedOn { get; set; }
        public string ExitCode { get; set; } = string.Empty;
        public Uri Uri { get; set; }

        public CrashMonitoringFile(string name, Uri uri, DateTimeOffset? createdOn)
        {
            Name = name;
            CreatedOn = createdOn;
            Uri = uri;

            var fileArray = Name.Split('_');
            var exitCodeIndex = fileArray.Length - 3;
            if (exitCodeIndex > 0)
            {
                ExitCode = fileArray[exitCodeIndex];
            }

        }
    }
}
