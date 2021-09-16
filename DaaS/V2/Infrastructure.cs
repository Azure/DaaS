// -----------------------------------------------------------------------
// <copyright file="Infrastructure.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;

namespace DaaS.V2
{

    internal class Infrastructure
    {
        private static Settings _settings;
        private static string _instanceId;

        internal static Settings Settings
        {
            get { return _settings ?? (_settings = Settings.Instance); }
            set { _settings = value; }
        }

        internal static string GetInstanceId()
        {
            if (string.IsNullOrWhiteSpace(_instanceId))
            {
                _instanceId = Environment.MachineName;
            }

            return _instanceId;
        }

        internal static string GetDaasInstallationPath()
        {
            var latestDaasDir = string.Empty;
            bool foundDaasAsPrivateExtension = false;
            if (Directory.Exists(EnvironmentVariables.PrivateSiteExtensionPath)
                && Directory.Exists(Path.Combine(EnvironmentVariables.PrivateSiteExtensionPath, "bin"))
                && Directory.Exists(Path.Combine(EnvironmentVariables.PrivateSiteExtensionPath, "bin", "DiagnosticTools")))
            {
                foundDaasAsPrivateExtension = true;
                latestDaasDir = EnvironmentVariables.PrivateSiteExtensionPath;
            }

            if (foundDaasAsPrivateExtension == false)
            {
                var programFiles = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
                if (programFiles == null)
                {
                    throw new ApplicationException("Program Files folder not found");
                }
                var rootDaasDir = Path.Combine(programFiles, @"SiteExtensions\DaaS");

                if (!Directory.Exists(rootDaasDir))
                {
                    // Must be in test mode. Assume the current directory has everything needed
                    return @".\";
                }

                var daasVersions = Directory.EnumerateDirectories(rootDaasDir).ToList();
                daasVersions.Sort();
                latestDaasDir = daasVersions.Last();
            }

            return latestDaasDir;
        }
    }
}
