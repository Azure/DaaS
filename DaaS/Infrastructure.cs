//-----------------------------------------------------------------------
// <copyright file="Infrastructure.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using DaaS.Configuration;
using DaaS.Leases;
using DaaS.Storage;

namespace DaaS
{
    internal class Infrastructure
    {
        private static Settings _settings;
        internal static Settings Settings
        {
            get { return _settings ?? (_settings = Settings.Instance); }
            set { _settings = value; }
        }

        private static IStorageController _storage;
        internal static IStorageController Storage
        {
            get { return _storage ?? (_storage = StorageController.Instance); }
            set { _storage = value; }
        }

        private static ILeaseManager _leaseManager;
        internal static ILeaseManager LeaseManager
        {
            get { return _leaseManager ?? (_leaseManager = Leases.LeaseManager.Instance); }
            set { _leaseManager = value; }
        }

        internal static string GetDaasInstalationPath()
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

                var daasVersions = Directory.EnumerateDirectories(rootDaasDir).ToList();
                daasVersions.Sort();
                latestDaasDir = daasVersions.Last();
            }
            
            return latestDaasDir;
        }

        internal static Func<string, string, string, Process> RunProcess = RunProcessImplementation;

        private static Process RunProcessImplementation(string command, string arguments, string sessionId)
        {
            // Expand the environment varibles just in case the command has %
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
            Logger.LogDiagnostic("Starting process. FileName = {0}, Arguments = {1}, sessionId = {2}", process.StartInfo.FileName, process.StartInfo.Arguments, sessionId);
            process.Start();
            return process;
        }
    }
}
