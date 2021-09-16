// -----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;

namespace DaaS.V2
{
    public class Settings
    {
        const string DefaultSettingsFileName = "DiagnosticSettings.json";
        const string PrivateSettingsFilePath = "PrivateSettings.json";
        const string DefaultHostNameSandboxProperty = "SANDBOX_FUNCTION_RESOURCE_ID";
        const string WebSiteDaasStorageSasUri = "%WEBSITE_DAAS_STORAGE_SASURI%";

        private string _diagnosticToolsPath;
        private bool? _isSandboxAvailable;
        private string _siteName;

        public static Settings Instance = CreateSettingsInstance();
        public int HoursBetweenOldSessionsCleanup { get; set; } = 4;

        // TODO : This is the only setting that is currently read from the old file
        // hence no references to this exist in code yet.

        public int FrequencyToCheckForNewSessionsAtInSeconds { get; set; }
        public int MaxAnalyzerTimeInMinutes { get; set; }
        public int MaxSessionTimeInMinutes { get; set; }
        public int OrphanInstanceTimeoutInMinutes { get; set; }
        public int MaxSessionAgeInDays { get; set; }
        public int MaxSessionsToKeep { get; set; }
        public int MaxSessionsPerDay { get; set; }
        public int MaxSessionCountThresholdPeriodInMinutes { get; set; }
        public int MaxSessionCountInThresholdPeriod { get; set; }
        public Diagnoser[] Diagnosers { get; set; }
        public string DiagnosticToolsPath { get; set; }

        public string BlobSasUri
        {
            get
            {
                var envVarName = WebSiteDaasStorageSasUri.Replace("%", "");
                if (IsSandBoxAvailable())
                {
                    int copiedBytes = 0;
                    byte[] valueBuffer = new byte[4096];
                    if (GetSandboxProperty(envVarName, valueBuffer, valueBuffer.Length, 0, ref copiedBytes))
                    {
                        string value = Encoding.Unicode.GetString(valueBuffer, 0, copiedBytes);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                string envvar = Environment.GetEnvironmentVariable(envVarName);
                if (!string.IsNullOrWhiteSpace(envvar))
                {
                    return envvar;
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// This may may either return the Site Name or the full DefaultHostName.
        /// Use it with caution.
        /// </summary>
        public string DefaultHostName
        {
            get
            {
                var defaultScmHostName = DefaultScmHostName;
                if (!string.IsNullOrWhiteSpace(defaultScmHostName))
                {
                    return defaultScmHostName.Replace(".scm.", ".");
                }

                return SiteName;
            }
        }

        /// <summary>
        /// Caution: There is a possibility that Sandbox property may not point to the 
        /// correct host name so this property may return empty. Use it carefully.
        /// </summary>
        public string DefaultScmHostName
        {
            get
            {
                string defaultHostName = string.Empty;
                if (!IsSandBoxAvailable())
                {
                    return SiteName;
                }

                int copiedBytes = 0;
                byte[] valueBuffer = new byte[4096];
                if (GetSandboxProperty(DefaultHostNameSandboxProperty, valueBuffer, valueBuffer.Length, 0, ref copiedBytes))
                {
                    string value = Encoding.Unicode.GetString(valueBuffer, 0, copiedBytes);
                    defaultHostName = value;
                }

                return defaultHostName;
            }
        }

        private string SiteName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_siteName))
                {
                    _siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";
                }
                return _siteName;
            }
        }

        internal string GetDiagnosticToolsPath()
        {
            if (!string.IsNullOrWhiteSpace(DiagnosticToolsPath))
            {
                return DiagnosticToolsPath;
            }

            if (string.IsNullOrWhiteSpace(_diagnosticToolsPath))
            {
                string latestDaasDir = Infrastructure.GetDaasInstallationPath();
                _diagnosticToolsPath = Path.Combine(latestDaasDir, "bin", "DiagnosticTools");
            }

            return _diagnosticToolsPath;
        }

        private static Settings CreateSettingsInstance()
        {
            string defaultSettingsFile = GetSettingFilePath();
            if (!File.Exists(defaultSettingsFile))
            {
                throw new FileNotFoundException($"Cannot find configuration settings file at {defaultSettingsFile}");
            }

            var defaultSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(defaultSettingsFile));
            var privateSettings = GetPrivateSettings();
            MergeSettings(defaultSettings, privateSettings);
            return defaultSettings;
        }

        private static void MergeSettings(Settings defaultSettings, Settings privateSettings)
        {
            //
            // If the PrivateSettings file is just created or is corrupt, the object may be null
            //

            if (privateSettings == null)
            {
                return;
            }

            var properties = typeof(Settings).GetProperties().Where(prop => prop.CanRead && prop.CanWrite);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(privateSettings, null);
                if (value != null)
                {
                    if (value is int intValue && intValue == 0)
                    {
                        continue;
                    }

                    if (prop.PropertyType.IsArray)
                    {
                        continue;
                    }
                    else
                    {
                        prop.SetValue(defaultSettings, value, null);
                    }
                }
            }

            if (privateSettings.Diagnosers != null && privateSettings.Diagnosers.Any())
            {
                defaultSettings.Diagnosers = defaultSettings.Diagnosers.Union(privateSettings.Diagnosers).ToArray();
            }
        }

        private static Settings GetPrivateSettings()
        {
            var settingsPath = CreatePrivateSettingsFileIfNotExists();

            try
            {
                var privateSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath));
                return privateSettings;
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while parsing private settings file, trying to overwrite the existing file", ex);
                OverwritePrivateSettings();
            }

            return null;
        }

        private static void OverwritePrivateSettings()
        {
            try
            {
                CreatePrivateSettingsFileIfNotExists(overwriteFile: true);
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while overwriting private settings file", ex);
            }
        }

        private static string CreatePrivateSettingsFileIfNotExists(bool overwriteFile = false)
        {
            var fullPath = Path.Combine(DaasDirectory.DaasPath, PrivateSettingsFilePath);
            if (File.Exists(fullPath) && !overwriteFile)
            {
                return fullPath;
            }

            if (overwriteFile)
            {
                FileSystemHelpers.DeleteFileSafe(fullPath);
            }

            using (Stream stream = typeof(Settings).Assembly.GetManifestResourceStream("DaaS.Configuration.PrivateSettings.json"))
            {
                using (FileStream file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.CopyTo(file);
                }
            }

            return fullPath;
        }

        private static string GetSettingFilePath()
        {
            string defaultPath = Path.Combine(
                Infrastructure.GetDaasInstallationPath(),
                @"bin\Configuration",
                DefaultSettingsFileName);

            if (!File.Exists(defaultPath))
            {
                var alternatePath = Path.Combine(GetAssemblyDirectory(), "Configuration", DefaultSettingsFileName);
                Logger.LogInfo($"Could not not find the file {defaultPath}, checking {alternatePath}");
                defaultPath = alternatePath;
            }

            return defaultPath;
        }

        private static string GetAssemblyDirectory()
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            return Path.GetDirectoryName(path);
        }

        internal bool IsSandBoxAvailable()
        {
            if (!_isSandboxAvailable.HasValue)
            {
                _isSandboxAvailable = File.Exists(Environment.ExpandEnvironmentVariables(@"%windir%\system32\picohelper.dll"));
            }

            return _isSandboxAvailable.Value;
        }

        [DllImport("picohelper.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetSandboxProperty(
           string propertyId,
           byte[] valueBuffer,
           int valueBufferLength,
           uint flags,
           ref int copiedBytes);
    }
}
