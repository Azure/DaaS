// -----------------------------------------------------------------------
// <copyright file="PredefinedValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DaaS.Diagnostics
{
    class PredefinedValidators
    {
        const string JavaToolsMissingMessage = "We identified the JDK tools required to collect diagnostics information are missing for the current version of JAVA runtime used for the app. Please download the JAVA SDK for the correct version for Java and run the tools manually via KUDU console.";
        const string JavaToolsUseFlightRecorder = "Your app is using a Java version that supports Java Flight Recorder tool (jcmd.exe). It is strongly recommended to use Java Flight recorder to debug Java issues.";
        private const string MemoryCacheKey = "JarFilePath";

        private bool CheckFileInLogFilesDirectory(string fileName)
        {
            string homePath = Environment.GetEnvironmentVariable("HOME_EXPANDED");

            if (homePath == null)
            {
                return false;
            }

            string strEventLogXmlPath = Path.Combine(homePath, "LogFiles", fileName);
            return File.Exists(strEventLogXmlPath);
        }
        
        public bool JavaMemoryDumpValidator(out string AdditionalError)
        {
            return CheckJavaProcessAndTools("jmap.exe", out AdditionalError);
        }

        public bool JavaMemoryStatisticsValidator(out string AdditionalError)
        {
            return CheckJavaProcessAndTools("jmap.exe", out AdditionalError);
        }

        public bool JavaThreadDumpValidator(out string AdditionalError)
        {
            return CheckJavaProcessAndTools("jstack.exe", out AdditionalError);
        }

        public bool JavaFlightRecorderValidator(out string AdditionalError)
        {
            return CheckJavaProcessAndTools("jcmd.exe", out AdditionalError);
        }

        private string GetJavaFolderPathFromConfig(out bool pathInConfigNotJavaExe)
        {
            pathInConfigNotJavaExe = false;
            string javaExePath = string.Empty;
            string webConfigPath = EnvironmentVariables.WebConfigFilePath;
            if (File.Exists(webConfigPath))
            {
                var xdocument = XDocument.Load(webConfigPath);
                var httpPlatform = xdocument.Descendants().Where(p => p.Name.LocalName == "httpPlatform").FirstOrDefault();
                if (httpPlatform != null && httpPlatform.Attribute("processPath") != null)
                {
                    var processPath = (string)httpPlatform.Attribute("processPath");
                    processPath = processPath.Contains("%") ? Environment.ExpandEnvironmentVariables(processPath) : processPath;
                    if (processPath.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        javaExePath = Path.GetDirectoryName(processPath);
                    }
                    else
                    {
                        pathInConfigNotJavaExe = true;
                    }
                }
            }
            return javaExePath;
        }

        private bool CheckToolExists(string toolPath, string toolName, out string additionalError)
        {
            additionalError = string.Empty;
            var javaToolPath = Path.Combine(toolPath, toolName);
            if (File.Exists(javaToolPath))
            {
                return true;
            }

            var jcmdPath = Path.Combine(toolPath, "jcmd.exe");
            additionalError = GetAdditionalError(toolName, jcmdPath);
            return false;
        }

        private bool CheckJavaProcessAndTools(string toolName, out string AdditionalError)
        {
            AdditionalError = string.Empty;
            var javaProcess = Process.GetProcessesByName("java").FirstOrDefault();
            if (javaProcess == null)
            {
                javaProcess = Process.GetProcessesByName("javaw").FirstOrDefault();
            }

            if (javaProcess == null)
            {
                return false;
            }

            var javaFolderPath = GetJavaFolderPathFromConfig(out bool pathInConfigNotJavaExe);
            if (!string.IsNullOrWhiteSpace(javaFolderPath))
            {

                return CheckToolExists(javaFolderPath, toolName, out AdditionalError);
            }
            else
            {
                //
                // Try to get the path of rt.jar by running KuduHandles on java.exe
                //

                var rtJarHandle = GetJarFileHandle(javaProcess.Id);
                if (!string.IsNullOrWhiteSpace(rtJarHandle) && rtJarHandle.Length > 0)
                {
                    var parentPath = rtJarHandle.Replace(@"\jre\lib\rt.jar", "").Replace("c:", GetOsDrive());
                    var javaToolPath = Path.Combine(parentPath, "bin");
                    return CheckToolExists(javaToolPath, toolName, out AdditionalError);
                }
                else
                {
                    //
                    // We failed to get the path of rt.jar from KuduHandles.exe
                    // Fallback to Environment variables
                    //

                    var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                    if (javaHome == null)
                    {
                        AdditionalError = JavaToolsMissingMessage;
                        return false;
                    }
                    else
                    {
                        var javaToolPath = Path.Combine(javaHome, "bin");
                        return CheckToolExists(javaToolPath, toolName, out AdditionalError);
                    }
                }
            }
        }

        private string GetOsDrive()
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
            if (!string.IsNullOrWhiteSpace(systemDrive))
            {
                return systemDrive;
            }

            return string.Empty;
        }

        private string GetJarFileHandle(int processId)
        {
            ObjectCache cache = MemoryCache.Default;
            if (cache[MemoryCacheKey] == null)
            {
                var jarFilePath = GetOpenFileHandles(processId).Where(x => x.EndsWith(@"\jre\lib\rt.jar")).FirstOrDefault();
                var cacheValue = string.IsNullOrWhiteSpace(jarFilePath) ? "EMPTY" : jarFilePath;
                var cachePolicy = new CacheItemPolicy
                {
                    AbsoluteExpiration = new DateTimeOffset(DateTime.UtcNow.AddMinutes(5))
                };
                cache.Add(MemoryCacheKey, cacheValue, cachePolicy);
                return jarFilePath;
            }
            else
            {
                var valueFromCache = (string)cache[MemoryCacheKey];
                return valueFromCache == "EMPTY" ? string.Empty : valueFromCache;
            }
        }

        private string GetAdditionalError(string toolName, string jcmdPath)
        {
            string additionalError = JavaToolsMissingMessage;
            if (!toolName.Equals("jcmd.exe", StringComparison.InvariantCultureIgnoreCase))
            {
                if (File.Exists(jcmdPath))
                {
                    additionalError = JavaToolsUseFlightRecorder;
                }
            }
            return additionalError;
        }

        public bool PhpErrorLogValidator()
        {
            return CheckFileInLogFilesDirectory("php_errors.log");
        }

        private IEnumerable<string> GetOpenFileHandles(int processId)
        {
            MemoryStream outputStream;
            MemoryStream errorStream;

            try
            {
                var cancellationTokenSource = new CancellationTokenSource();
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "KuduHandles.exe",
                        Arguments = processId.ToString(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                outputStream = new MemoryStream();
                errorStream = new MemoryStream();

                process.Start();

                var tasks = new List<Task>
                {
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardOutput.BaseStream, outputStream, cancellationTokenSource.Token),
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardError.BaseStream, errorStream, cancellationTokenSource.Token)
                };

                process.WaitForExit(2 * 1000);
                if (process != null && !process.HasExited)
                {
                    process.SafeKillProcess();
                }

                Task.WhenAll(tasks);

                string output = outputStream.ReadToEnd();
                string error = errorStream.ReadToEnd();

                Logger.LogInfo(output);

                if (!string.IsNullOrEmpty(error))
                {
                    Logger.LogInfo(error);
                }

                if (process.ExitCode != 0)
                {
                    Logger.LogVerboseEvent($"Starting process KuduHandles failed with the following error code '{process.ExitCode}'");
                    return Enumerable.Empty<string>();
                }
                else
                {
                    return output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Unhandled exception in GetOpenFileHandles method", ex);
            }
            return Enumerable.Empty<string>();
        }
    }
}
