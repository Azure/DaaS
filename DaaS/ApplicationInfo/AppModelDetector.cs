// -----------------------------------------------------------------------
// <copyright file="AppModelDetector.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DaaS.ApplicationInfo
{
    public class AppModelDetector
    {
        // We use Hosting package to detect AspNetCore version
        // it contains light-up implemenation that we care about and
        // would have to be used by aspnet core web apps
        private const string AspNetCoreAssembly = "Microsoft.AspNetCore.Hosting";
        private readonly string[] AdditionalConfigurationSections = new string[] { "monitoring", "applicationInitialization", "rewrite", "aspNetCore", "httpRuntime"};

        /// <summary>
        /// Reads the following sources
        ///     - web.config to detect dotnet framework kind
        ///     - *.runtimeconfig.json to detect target framework version
        ///     - *.deps.json to detect Asp.Net Core version
        ///     - Microsoft.AspNetCore.Hosting.dll to detect Asp.Net Core version
        /// </summary>
        /// <param name="directory">The application directory</param>
        /// <returns>The <see cref="AppModelDetectionResult"/> instance containing information about application</returns>
        public AppModelDetectionResult Detect(DirectoryInfo directory)
        {
            string entryPoint = null;

            // Try reading web.config and resolving framework and app path
            var webConfig = directory.GetFiles("web.config").FirstOrDefault();

            bool webConfigExists = webConfig != null;
            bool? usesDotnetExe = null;

            var webConfigSections = new List<WebConfigSection>();

            string processPath = "";
            if (webConfigExists &&
            TryParseWebConfig(webConfig, out var dotnetExe, out entryPoint, out processPath, out webConfigSections))
            {
                usesDotnetExe = dotnetExe;
            }

            // If we found entry point let's look for .deps.json
            // in some cases it exists in desktop too
            FileInfo depsJson = null;
            FileInfo runtimeConfig = null;
            FileInfo appSettingsJson = null;
            string loggingLevel = "";

            try
            {
                if (!string.IsNullOrWhiteSpace(entryPoint))
                {
                    depsJson = new FileInfo(Path.ChangeExtension(entryPoint, ".deps.json"));
                    runtimeConfig = new FileInfo(Path.ChangeExtension(entryPoint, ".runtimeconfig.json"));

                    appSettingsJson = new FileInfo(Path.Combine(Path.GetDirectoryName(entryPoint), "appsettings.json"));
                    if (appSettingsJson != null && appSettingsJson.Exists)
                    {
                        using (var streamReader = appSettingsJson.OpenText())
                        using (var jsonReader = new JsonTextReader(streamReader))
                        {
                            var json = JObject.Load(jsonReader);
                            if (json?["Logging"] != null && json?["Logging"]["LogLevel"] != null && json?["Logging"]["LogLevel"]?["Default"] != null)
                            {
                                loggingLevel = (string)json?["Logging"]["LogLevel"]?["Default"];
                                if (!string.IsNullOrWhiteSpace(loggingLevel))
                                {
                                    Logger.LogVerboseEvent($"LoggedEnabled section set to {loggingLevel} in .net core config");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("AppModelDetector: Failed while checking logging level", ex);
            }

            if (depsJson == null || !depsJson.Exists)
            {
                depsJson = directory.GetFiles("*.deps.json").FirstOrDefault();
            }

            if (runtimeConfig == null || !runtimeConfig.Exists)
            {
                runtimeConfig = directory.GetFiles("*.runtimeconfig.json").FirstOrDefault();
            }

            string aspNetCoreVersionFromDeps = null;
            string aspNetCoreVersionFromDll = null;

            try
            {
                // Try to detect ASP.NET Core version from .deps.json
                if (depsJson != null &&
                    depsJson.Exists &&
                    TryParseDependencies(depsJson, out var aspNetCoreVersion))
                {
                    aspNetCoreVersionFromDeps = aspNetCoreVersion;
                }

                // Try to detect ASP.NET Core version from .deps.json
                var aspNetCoreDll = directory.GetFiles(AspNetCoreAssembly + ".dll").FirstOrDefault();
                if (aspNetCoreDll != null &&
                    TryParseAssembly(aspNetCoreDll, out aspNetCoreVersion))
                {
                    aspNetCoreVersionFromDll = aspNetCoreVersion;
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("AppModelDetector: Failed while detecting core version from deps.json", ex);
            }

            var result = new AppModelDetectionResult();
            try
            {
                // Try to detect dotnet core runtime version from runtimeconfig.json
                string runtimeVersionFromRuntimeConfig = null;
                if (runtimeConfig != null &&
                    runtimeConfig.Exists)
                {
                    TryParseRuntimeConfig(runtimeConfig, out runtimeVersionFromRuntimeConfig);
                }


                result.LoggingLevel = loggingLevel;
                result.CoreProcessName = processPath;
                if (usesDotnetExe == true)
                {
                    result.Framework = RuntimeFramework.DotNetCore;
                    result.FrameworkVersion = runtimeVersionFromRuntimeConfig;
                }
                else
                {
                    if (depsJson?.Exists == true &&
                        runtimeConfig?.Exists == true)
                    {
                        result.Framework = RuntimeFramework.DotNetCoreStandalone;
                    }
                    else
                    {
                        result.Framework = RuntimeFramework.DotNetFramework;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("AppModelDetector: Failed while detecting core version from runtimeconfig.json", ex);
            }

            result.AspNetCoreVersion = aspNetCoreVersionFromDeps ?? aspNetCoreVersionFromDll;
            result.WebConfigSections = webConfigSections;
            return result;
        }

        private bool TryParseAssembly(FileInfo aspNetCoreDll, out string aspNetCoreVersion)
        {
            aspNetCoreVersion = null;
            try
            {
                using (var stream = aspNetCoreDll.OpenRead())
                {
                    aspNetCoreVersion = AssemblyName.GetAssemblyName(aspNetCoreDll.FullName).Version.ToString();
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Search for Microsoft.AspNetCore.Hosting entry in deps.json and get it's version number
        /// </summary>
        private bool TryParseDependencies(FileInfo depsJson, out string aspnetCoreVersion)
        {
            aspnetCoreVersion = null;
            try
            {
                using (var streamReader = depsJson.OpenText())
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    var json = JObject.Load(jsonReader);

                    var libraryPrefix = AspNetCoreAssembly + "/";

                    var library = json.Descendants().OfType<JProperty>().FirstOrDefault(property => property.Name.StartsWith(libraryPrefix));
                    if (library != null)
                    {
                        aspnetCoreVersion = library.Name.Substring(libraryPrefix.Length);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }
            return false;
        }

        private bool TryParseRuntimeConfig(FileInfo runtimeConfig, out string frameworkVersion)
        {
            frameworkVersion = null;
            try
            {
                using (var streamReader = runtimeConfig.OpenText())
                using (var jsonReader = new JsonTextReader(streamReader))
                {
                    var json = JObject.Load(jsonReader);
                    frameworkVersion = (string)json?["runtimeOptions"]
                        ?["framework"]
                        ?["version"];

                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryParseWebConfig(FileInfo webConfig, out bool usesDotnetExe, out string entryPoint, out string processPath, out List<WebConfigSection> sectionConfigurations)
        {
            sectionConfigurations = new List<WebConfigSection>();
            usesDotnetExe = false;
            entryPoint = null;
            processPath = null;

            try
            {
                var xdocument = XDocument.Load(webConfig.FullName);
                var aspNetCoreHandler = xdocument.Descendants().Where(p => p.Name.LocalName == "aspNetCore").FirstOrDefault();

                if (aspNetCoreHandler != null)
                {
                    processPath = (string)aspNetCoreHandler.Attribute("processPath");
                    processPath = Path.GetFileNameWithoutExtension(processPath).ToLower();
                    var arguments = (string)aspNetCoreHandler.Attribute("arguments");

                    if (processPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase) ||
                        processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(arguments))
                    {
                        usesDotnetExe = true;
                        var entryPointPart = arguments.Split(new[] { " " }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

                        if (!string.IsNullOrWhiteSpace(entryPointPart))
                        {
                            try
                            {
                                entryPoint = Path.GetFullPath(Path.Combine(webConfig.DirectoryName, entryPointPart));
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                    else
                    {
                        usesDotnetExe = false;

                        try
                        {
                            entryPoint = Path.GetFullPath(Path.Combine(webConfig.DirectoryName, processPath));
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                foreach (var item in AdditionalConfigurationSections)
                {
                    var additionalSection = xdocument.Descendants(item).FirstOrDefault();
                    if (additionalSection != null)
                    {
                        var sectionConfiguration = new WebConfigSection
                        {
                            SectionName = item,
                            Configuration = additionalSection.ToString()
                        };
                        sectionConfigurations.Add(sectionConfiguration);
                    }

                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("AppModelDetector: While checking for .NET core, failed to parse web.config with the error", ex);
                return false;
            }

            return true;
        }
    }
}
