// -----------------------------------------------------------------------
// <copyright file="Infrastructure.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using DaaS.Configuration;

namespace DaaS
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
                    throw new InstanceNotFoundException("What happened to Program Files?");
                }
                var rootDaasDir = Path.Combine(programFiles, @"SiteExtensions\DaaS");
                if (!Directory.Exists(rootDaasDir))
                {
                    // Must be in test mode. Assume the current directory has everything needed
                    return @".\";
                }

                var daasSiteExtensionDirectories = Directory.EnumerateDirectories(rootDaasDir).ToList();
                var versions = new Dictionary<Version, string>();
                foreach (var daasSiteExtensionDirectory in daasSiteExtensionDirectories)
                {
                    var directoryName = new DirectoryInfo(daasSiteExtensionDirectory).Name;
                    versions.Add(new Version(directoryName), daasSiteExtensionDirectory);
                }
                
                var highestVersion = versions.OrderByDescending(x => x.Key).FirstOrDefault();
                latestDaasDir = highestVersion.Value;
            }
            
            return latestDaasDir;
        }

        internal static Process RunProcess(string command, string arguments, string sessionId, string description = "")
        {
            return RunProcessImplementation(command, arguments, sessionId, description);
        }

        private static Process RunProcessImplementation(string command, string arguments, string sessionId, string description = "")
        {
            // Expand the environment variables just in case the command has %
            command = Environment.ExpandEnvironmentVariables(command);
            
            var process = new Process()
            {
                StartInfo =
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false
                }
            };

            process.StartInfo.EnvironmentVariables.Add("DAAS_SESSION_ID",sessionId);
            process.StartInfo.EnvironmentVariables.Add("DAAS_SESSION_DESCRIPTION", description);
            Logger.LogDiagnostic("Starting process. FileName = {0}, Arguments = {1}, sessionId = {2}", process.StartInfo.FileName, process.StartInfo.Arguments, sessionId);
            process.Start();
            return process;
        }
    }
}
