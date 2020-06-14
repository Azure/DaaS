//-----------------------------------------------------------------------
// <copyright file="Infrastructure.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Instrumentation;
using System.Text;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.HeartBeats;
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
            if (Directory.Exists(@"D:\home\SiteExtensions\daas"))
            {
                foundDaasAsPrivateExtension = Directory.Exists(@"D:\home\SiteExtensions\daas\bin");
                foundDaasAsPrivateExtension = Directory.Exists(@"D:\home\SiteExtensions\daas\bin\DiagnosticTools");
                foundDaasAsPrivateExtension = true;
                latestDaasDir = @"D:\home\SiteExtensions\daas";
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

        //private static IHeartBeatController _heartBeatController;

        //public static IHeartBeatController HeartBeatController
        //{
        //    get { return _heartBeatController ?? (_heartBeatController = HeartBeats.HeartBeatController.Instance); }
        //    set { _heartBeatController = value; }
        //}

        //internal static Func<string, string, Task> RunProcessAsync = RunProcessAsyncImplementation;

        internal static Func<string, string, string, Process> RunProcess = RunProcessImplementation;

        private static Process RunProcessImplementation(string command, string arguments, string sessionId)
        {
            var process = new Process()
            {
                StartInfo =
                {
                    FileName = command,
                    Arguments = arguments,
                    //WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,                    
                    //RedirectStandardError = true,
                    //RedirectStandardOutput = true,
                }

                
            };

            process.StartInfo.EnvironmentVariables.Add("DAAS_SESSION_ID",sessionId);
            Logger.LogDiagnostic("Starting process. FileName = {0}, Arguments = {1}, sessionId = {2}", process.StartInfo.FileName, process.StartInfo.Arguments, sessionId);
            process.Start();
            return process;
        }

        ///// <summary>
        ///// Used to be able to determine when the process has stopped running without having to depend on a Process type (for testing)
        ///// </summary>
        //static Task RunProcessAsyncImplementation(string command, string arguments)
        //{
        //    // there is no non-generic TaskCompletionSource
        //    var tcs = new TaskCompletionSource<bool>();

        //    var process = new Process()
        //    {
        //        StartInfo =
        //        {
        //            FileName = command,
        //            Arguments = arguments,
        //            //WindowStyle = ProcessWindowStyle.Hidden,
        //            UseShellExecute = false,
        //            RedirectStandardError = true,
        //            RedirectStandardOutput = true,
        //        },
        //        EnableRaisingEvents = true
        //    };

        //    process.Exited += (sender, args) =>
        //    {
        //        var errorStream = process.StandardError;
        //        var errorText = errorStream.ReadToEnd();
        //        if (!string.IsNullOrEmpty(errorText))
        //        {
        //            var errorCode = process.ExitCode;
        //            process.Dispose();
        //            throw new ApplicationException(
        //                string.Format(
        //                "Process {0} exited with error code {1}. Error message: {2}",
        //                command,
        //                errorCode,
        //                errorText));
        //        }

        //        tcs.SetResult(true);
        //        process.Dispose();
        //    };

        //    process.Start();

        //    return tcs.Task;
        //}
    }
}
