//-----------------------------------------------------------------------
// <copyright file="PredefinedValidators.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DaaS
{
    class PredefinedValidators
    {
        const string JavaToolsMissingMessage = "We identified the JDK tools required to collect diagnostics information are missing for the current version of JAVA runtime used for the app. Please download the JAVA SDK for the correct version for Java and run the tools manually via KUDU console.";
        const string JavaToolsUseFlightRecorder = "Your app is using a Java version that supports Java Flight Recorder tool (jcmd.exe). It is strongly recommended to use Java Flight recorder to debug Java issues.";
        private bool CheckFileInLogFilesDirectory(string fileName)
        {
            string homePath = Environment.GetEnvironmentVariable("HOME_EXPANDED");

            if (homePath == null)
            {
                return false;
            }
            else
            {
                string strEventLogXmlPath = Path.Combine(homePath, "LogFiles", fileName);
                if (File.Exists(strEventLogXmlPath))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        public bool EventViewerValidator(out string AdditionalError)
        {
            AdditionalError = string.Empty;
            return CheckFileInLogFilesDirectory("eventlog.xml");
        }

        public bool JMapDumpCollectorValidator(out string AdditionalError)
        {
            bool toolsExist = CheckJavaProcessAndTools("jmap.exe", out AdditionalError);
            return toolsExist;
        }

        public bool JMapStatsCollectorValidator(out string AdditionalError)
        {
            bool toolsExist = CheckJavaProcessAndTools("jmap.exe", out AdditionalError);
            return toolsExist;
        }

        public bool JStackCollectorValidator(out string AdditionalError)
        {
            bool toolsExist = CheckJavaProcessAndTools("jstack.exe", out AdditionalError);
            return toolsExist;
        }
        public bool JCmdCollectorValidator(out string AdditionalError)
        {
            bool toolsExist = CheckJavaProcessAndTools("jcmd.exe", out AdditionalError);
            return toolsExist;
        }

        private string GetJavaFolderPathFromConfig(out bool pathInConfigNotJavaExe)
        {
            pathInConfigNotJavaExe = false;
            string javaExePath = string.Empty;
            string webConfigPath = @"d:\\home\\site\\wwwroot\\web.config";
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

        private bool CheckJavaProcessAndTools(string toolName, out string AdditionalError)
        {
            AdditionalError = string.Empty;
            var javaProcess = Process.GetProcessesByName("java").FirstOrDefault();
            if (javaProcess == null)
            {
                return false;
            }
                
            var javaFolderPath = GetJavaFolderPathFromConfig(out bool pathInConfigNotJavaExe);
            if (!string.IsNullOrWhiteSpace(javaFolderPath))
            {
                var javaToolPath = Path.Combine(javaFolderPath, toolName);
                if (File.Exists(javaToolPath))
                {
                    return true;
                }
                else
                {
                    var jcmdPath = Path.Combine(javaFolderPath, "jcmd.exe");
                    AdditionalError = GetAdditionalError(toolName, jcmdPath);
                    return false;
                }
            }
            else
            {
                var rtJarHandle = GetOpenFileHandles(javaProcess.Id).Where(x => x.EndsWith(@"\jre\lib\rt.jar")).FirstOrDefault();
                if (rtJarHandle != null && rtJarHandle.Length > 0)
                {
                    var parentPath = rtJarHandle.Replace(@"\jre\lib\rt.jar", "").Replace("c:", "d:");
                    var javaToolPath = Path.Combine(parentPath, "bin", toolName);
                    if (File.Exists(javaToolPath))
                    {
                        return true;
                    }
                    else
                    {
                        var jcmdPath = Path.Combine(parentPath, "bin", "jcmd.exe");
                        AdditionalError = GetAdditionalError(toolName, jcmdPath);
                        return false;
                    }
                }
                else
                {
                    var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
                    if (javaHome == null)
                    {
                        AdditionalError = JavaToolsMissingMessage;
                        return false;
                    }
                    else
                    {
                        var javaToolPath = Path.Combine(javaHome, "bin", toolName);
                        if (File.Exists(javaToolPath))
                        {
                            return true;
                        }
                        else
                        {
                            var jcmdPath = Path.Combine(javaHome, "bin", "jcmd.exe");
                            AdditionalError = GetAdditionalError(toolName, jcmdPath);
                            return false;
                        }
                    }
                }
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

        public bool HttpLogsCollectorValidator()
        {
            string httploggingEnabled = Environment.GetEnvironmentVariable("WEBSITE_HTTPLOGGING_ENABLED");
            if (httploggingEnabled != null && httploggingEnabled.ToString() == "1")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public bool PhpErrorLogCollectorValidator()
        {
            return CheckFileInLogFilesDirectory("php_errors.log");
        }

        private IEnumerable<string> GetOpenFileHandles(int processId)
        {
            MemoryStream outputStream = null;
            MemoryStream errorStream = null;

            try
            {
                var cancellationTokenSource = new CancellationTokenSource();
                Process process = new Process();

                ProcessStartInfo pinfo = new ProcessStartInfo
                {
                    FileName = "KuduHandles.exe",
                    Arguments = processId.ToString()
                };

                process.StartInfo = pinfo;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                outputStream = new MemoryStream();
                errorStream = new MemoryStream();

                process.Start();

                var tasks = new List<Task>
                {
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardOutput.BaseStream, outputStream, cancellationTokenSource.Token),
                    MemoryStreamExtensions.CopyStreamAsync(process.StandardError.BaseStream, errorStream, cancellationTokenSource.Token)
                };
                process.WaitForExit(5 * 1000);
                if (process!= null && !process.HasExited)
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
                    return output.Split(new[] { System.Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}
