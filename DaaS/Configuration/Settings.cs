//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using DaaS.Diagnostics;
using DaaS.Storage;

namespace DaaS.Configuration
{
    static class SettingsXml
    {
        internal const string DiagnosticSettings = "DiagnosticSettings";
        internal const string Settings = "Settings";
        internal const string Diagnosers = "Diagnosers";
        internal const string Diagnoser = "Diagnoser";
        internal const string Collectors = "Collectors";
        internal const string Collector = "Collector";
        internal const string Analyzers = "Analyzers";
        internal const string Analyzer = "Analyzer";
        internal const string Name = "Name";
        internal const string Description = "Description";
        internal const string ProcessCleanupOnCancel = "ProcessCleanupOnCancel";
        internal const string DiagnoserRequiresStorage = "DiagnoserRequiresStorage";
    }

    static class DaaSSettings
    {
        internal const string LeaseDurationInSeconds = "LeaseDurationInSeconds";
        internal const string BlobStorageSas = "BlobStorageSas";
        internal const string HeartBeatLifeTimeInSeconds = "HeartBeatLifeTimeInSeconds";
        internal const string FrequencyToCheckForNewSessionsAtInSeconds = "FrequencyToCheckForNewSessionsAtInSeconds";
        internal const string MaxDiagnosticToolRetryCount = "MaxDiagnosticToolRetryCount";
        internal const string MaxDiagnosticSessionsToKeep = "MaxDiagnosticSessionsToKeep";
        internal const string MaxAnalyzerTimeInMinutes = "MaxAnalyzerTimeInMinutes";
        internal const string MaxSessionTimeInMinutes = "MaxSessionTimeInMinutes";
        internal const string MaxNumberOfDaysForSessions = "MaxNumberOfDaysForSessions";
        internal const string MaxSessionCountInThresholdPeriod = "MaxSessionCountInThresholdPeriod";
        internal const string MaxSessionCountThresholdPeriodInMinutes = "MaxSessionCountThresholdPeriodInMinutes";
        internal const string MaxSessionsPerDay = "MaxSessionsPerDay";

    }

    public class Settings
    {
        [DllImport("picohelper.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public extern static bool GetSandboxProperty(
           string propertyId,
           byte[] valueBuffer,
           int valueBufferLength,
           uint flags,
           ref int copiedBytes);

        const string PrivateSettingsFilePath = @"PrivateSettings.xml";
        const string DefaultSettingsFileName = @"DiagnosticSettings.xml";

        public const string WebSiteDaasStorageSasUri = "%WEBSITE_DAAS_STORAGE_SASURI%";
        public const string CancelledDir = @"Cancelled";
        public const string DefaultHostNameSandboxProperty = "SANDBOX_FUNCTION_RESOURCE_ID";

        public static Settings Instance = new Settings();

        public static object _settingsLock = new object();

        private static XDocument DefaultSettingsXmlCache;
        private static XDocument PrivateSettingsXmlCache;

        public string BlobStorageSas
        {
            get
            {
                return GetSetting(DaaSSettings.BlobStorageSas, true);
            }
            set
            {
                SaveSetting(DaaSSettings.BlobStorageSas, value);
            }
        }

        public string BlobStorageSasSpecifiedAt
        {
            get
            {
                if (IsBlobSasUriConfiguredAsEnvironmentVariable())
                {
                    return "EnvironmentVariable";
                }
                else
                {
                    return !string.IsNullOrWhiteSpace(Infrastructure.Settings.BlobStorageSas) ? "PrivateSettings.xml" : string.Empty;
                }
            }
        }

        public static bool IsBlobSasUriConfiguredAsEnvironmentVariable()
        {
            GetBlobSasUriFromEnvironment(out bool configuredAsEnvironmentVariable);
            return configuredAsEnvironmentVariable;
        }

        public static string GetBlobSasUriFromEnvironment(out bool configuredAsEnvironmentVariable)
        {
            configuredAsEnvironmentVariable = false;
            var environmentVariableName = WebSiteDaasStorageSasUri.Replace("%", "");

            if (IsSandBoxAvailable())
            {
                int copiedBytes = 0;
                byte[] valueBuffer = new byte[4096];
                if (GetSandboxProperty(environmentVariableName, valueBuffer, valueBuffer.Length, 0, ref copiedBytes))
                {
                    string value = Encoding.Unicode.GetString(valueBuffer, 0, copiedBytes);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        configuredAsEnvironmentVariable = true;
                        return value;
                    }
                }
            }

            string envvar = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(envvar))
            {
                configuredAsEnvironmentVariable = true;
                return envvar;
            }

            return string.Empty;
        }

        internal static bool IsSandBoxAvailable()
        {
            return System.IO.File.Exists(Environment.ExpandEnvironmentVariables(@"%windir%\system32\picohelper.dll"));
        }

        internal string GetRootStoragePathForLocation(StorageLocation location)
        {
            if (location == StorageLocation.UserSiteData)
            {
                return UserSiteStorageDirectory;
            }
            if (location == StorageLocation.UserSiteRoot)
            {
                return SiteRootDir;
            }
            if (location == StorageLocation.TempStorage)
            {
                return TempDir;
            }
            throw new Exception(string.Format("Unknown storage location {0} encountered", location));
        }

        internal static string UserSiteStorageDirectory
        {
            get
            {
                return Path.Combine(Settings.SiteRootDir, @"data\DaaS");
            }
        }

        public Settings() { }

        private static string _instanceName;
        internal static string InstanceName
        {
            get
            {
                if (_instanceName == null)
                {
                    _instanceName = Environment.GetEnvironmentVariable("COMPUTERNAME");
                }
                return _instanceName;
            }
        }

        private string _siteName;
        private string _defaultHostName;
        public string SiteName
        {
            get
            {
                if (_siteName == null)
                {
                    _siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";
                }
                return _siteName;
            }
            set
            {
                _siteName = value;
            }
        }
        public string SiteNameShort
        {
            get
            {
                //if (string.IsNullOrWhiteSpace(_defaultHostName))
                //{
                //    string val = Environment.GetEnvironmentVariable("HTTP_HOST");
                //    if (!string.IsNullOrWhiteSpace(val))
                //    {
                //        val = val.ToLower().Replace(".scm.", ".");
                //        _defaultHostName = val.Length > 50 ? val.Substring(0,50) : val;
                //    }
                //}

                //if (!string.IsNullOrWhiteSpace(_defaultHostName))
                //{
                //    return _defaultHostName;
                //}

                return ShortenString(SiteName);
            }
        }

        private static string _siteRootDir;
        internal static string SiteRootDir
        {
            get
            {
                if (string.IsNullOrEmpty(_siteRootDir))
                {
                    _siteRootDir = Environment.GetEnvironmentVariable("HOME_EXPANDED") ?? Path.Combine(Instance.TempDir, "DaaSRoot");
                }
                return _siteRootDir;
            }
            set
            {
                _siteRootDir = value;
            }
        }

        private string _tempDir;

        

        internal string TempDir
        {
            get
            {
                if (_tempDir == null)
                {
                    _tempDir = System.Environment.GetEnvironmentVariable("TEMP");
                }
                return _tempDir;
            }
            set
            {
                _tempDir = value;
            }
        }

        private int GetIntSetting(string settingName, int defaultValue)
        {
            string settingValueStr = GetSetting(settingName);
            int settingValue;
            if (!int.TryParse(settingValueStr, out settingValue))
            {
                settingValue = defaultValue;
            }
            return settingValue;
        }

        public virtual string GetSetting(string name, bool invalidateSettingsCache = false)
        {
            GetSettingXmls(invalidateSettingsCache, out XDocument defaultSettings, out XDocument privateSettings);

            // Use the value defined in the private settings file (if any). If nothing is defined then use the value in the default settings file
            string settingValue = null;
            int i = 0;

            var settingsXmlList = new List<XDocument>
            {
                defaultSettings
            };
            if (privateSettings !=null)
            {
                settingsXmlList.Add(privateSettings);
            }

            foreach (XDocument settingsXml in settingsXmlList)
            {
                i++;
                var diagnosticSettingsXml = settingsXml.Element(SettingsXml.DiagnosticSettings);
                if (diagnosticSettingsXml == null)
                {
                    continue;
                }
                var settingsBlockXml = diagnosticSettingsXml.Element(SettingsXml.Settings);
                if (settingsBlockXml != null)
                {
                    var settingXml = settingsBlockXml.Element(name);
                    //Logger.LogDiagnostic("Found setting block " + i);
                    if (settingXml == null)
                    {
                        continue;
                    }
                    settingValue = settingXml.Value;
                    break;
                }
            }

            return settingValue;
        }

        public virtual void SaveSetting(string name, string value)
        {
            XDocument settingsDoc = GetPrivateSettingsXml();

            if (settingsDoc == null)
            {
                throw new ApplicationException("Failed while reading the PrivateSettings.xml. Please retry the operation again");
            }

            var diagnosticSettingsXml = settingsDoc.Element(SettingsXml.DiagnosticSettings);
            if (diagnosticSettingsXml == null)
            {
                diagnosticSettingsXml = new XElement(SettingsXml.DiagnosticSettings);
                settingsDoc.Add(diagnosticSettingsXml);
            }
            var settingsBlockXml = diagnosticSettingsXml.Element(SettingsXml.Settings);
            if (settingsBlockXml == null)
            {
                settingsBlockXml = new XElement(SettingsXml.Settings);
                diagnosticSettingsXml.Add(settingsBlockXml);
            }

            var settingXml = settingsBlockXml.Element(name);
            if (settingXml == null)
            {
                settingXml = new XElement(name);
                settingsBlockXml.Add(settingXml);
            }

            settingXml.Value = value;

            using (Stream fileStream = new MemoryStream())
            {
                settingsDoc.Save(fileStream);
                fileStream.Position = 0;

                Infrastructure.Storage.SaveFile(fileStream, PrivateSettingsFilePath, StorageLocation.UserSiteData);

                InvalidateSettingsCache();
            }
        }

        private static void InvalidateSettingsCache()
        {
            lock (_settingsLock)
            {
                try
                {
                    DefaultSettingsXmlCache = null;
                    PrivateSettingsXmlCache = null;
                }
                catch (Exception e)
                {
                    Logger.LogErrorEvent("Failed while clearing settings cache", e);
                }

            }
        }

        internal TimeSpan LeaseDuration
        {
            get
            {
                int duration = GetIntSetting(DaaSSettings.LeaseDurationInSeconds, defaultValue: 15);
                return TimeSpan.FromSeconds(duration);
            }
        }

        internal TimeSpan LeaseRenewalTime
        {
            get
            {
                return TimeSpan.FromMilliseconds(LeaseDuration.TotalMilliseconds/2);
            }
        }

        internal TimeSpan HeartBeatLifeTime
        {
            get
            {
                int lifetime = GetIntSetting(DaaSSettings.HeartBeatLifeTimeInSeconds, defaultValue: 300);
                return TimeSpan.FromSeconds(lifetime);
            }
        }

        internal TimeSpan FrequencyToCheckForNewSessionsAt
        {
            get
            {
                int frequencyToCheckForNewSecondsAtInSeconds = GetIntSetting(DaaSSettings.FrequencyToCheckForNewSessionsAtInSeconds, defaultValue: 30);
                return TimeSpan.FromSeconds(frequencyToCheckForNewSecondsAtInSeconds);
            }
        }

        internal int MaxDiagnosticToolRetryCount
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxDiagnosticToolRetryCount, defaultValue: 3);
            }
        }

        internal int MaxDiagnosticSessionsToKeep
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxDiagnosticSessionsToKeep, defaultValue: 10);
            }
        }
        internal int MaxAnalyzerTimeInMinutes
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxAnalyzerTimeInMinutes, defaultValue: 20);
            }
        }
        internal int MaxSessionTimeInMinutes
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxSessionTimeInMinutes, defaultValue: 60);
            }
        }
        internal int MaxNumberOfDaysForSessions
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxNumberOfDaysForSessions, defaultValue: 20);
            }
        }
        internal int MaxSessionCountInThresholdPeriod
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxSessionCountInThresholdPeriod, defaultValue: 5);
            }
        }
        internal int MaxSessionCountThresholdPeriodInMinutes
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxSessionCountThresholdPeriodInMinutes, defaultValue: 120);
            }
        }

        internal int MaxSessionsPerDay
        {
            get
            {
                return GetIntSetting(DaaSSettings.MaxSessionsPerDay, defaultValue: 6);
            }
        }

        public static string DefaultHostName
        {
            get
            {
                var defaultHostName = GetDefaultHostName();
                if (!string.IsNullOrWhiteSpace(defaultHostName))
                {
                    return defaultHostName;
                }

                return Instance.SiteNameShort;
            }
        }

        internal static string GetDefaultHostName(bool fullHostName = false)
        {
            if (IsSandBoxAvailable())
            {
                int copiedBytes = 0;
                byte[] valueBuffer = new byte[4096];
                if (GetSandboxProperty(DefaultHostNameSandboxProperty, valueBuffer, valueBuffer.Length, 0, ref copiedBytes))
                {
                    string value = Encoding.Unicode.GetString(valueBuffer, 0, copiedBytes);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        if (fullHostName)
                        {
                            return value.Replace(".scm.", ".");
                        }

                        if (value.Contains("."))
                        {
                            value = value.Split('.')[0];
                        }

                        value = ShortenString(value);
                        return value;
                    }
                }
            }
            return string.Empty;
        }

        internal static string ShortenString(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length > 40 ? name.Substring(0, 40) : name;
        }

        internal string GetDiagnosticToolsPath()
        {
            var diagnosticToolsPath = GetSetting("DiagnosticToolsPath");

            if (string.IsNullOrWhiteSpace(diagnosticToolsPath))
            {
                // The diagnostic tools directory has not been specified. Calculate it instead
                string latestDaasDir = Infrastructure.GetDaasInstalationPath();
                diagnosticToolsPath = Path.Combine(latestDaasDir, "bin", "DiagnosticTools");
            }

            return diagnosticToolsPath;
        }

        public virtual IEnumerable<Diagnoser> GetDiagnosers()
        {
            var diagnosers = new Dictionary<String, Diagnoser>();
            Dictionary<String, Collector> collectors = null;
            Dictionary<String, Analyzer> analyzers = null;

            GetSettingXmls(false, out XDocument defaultSettingsXml, out XDocument privateSettingsXml);

            // Load the diagnostic tools in the default and private settings.  Any tools in the private settings override the tools in the default settings
            LoadDiagnosticTools(defaultSettingsXml, ref collectors, ref analyzers);

            if (privateSettingsXml != null)
            {
                LoadDiagnosticTools(privateSettingsXml, ref collectors, ref analyzers);
            }

            // Load the diagnosers from the default and private settings.  Any diagnosers in the private settings override the tools in the default settings
            foreach (var diagnoser in LoadDiagnosers(defaultSettingsXml, collectors, analyzers))
            {
                diagnosers[diagnoser.Name.ToLower()] = diagnoser;
            }

            try
            {
                foreach (var diagnoser in LoadDiagnosers(privateSettingsXml, collectors, analyzers))
                {
                    diagnosers[diagnoser.Name.ToLower()] = diagnoser;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while loading private diagnosers", ex);
            }

            return diagnosers.Values;
        }

        private static void GetSettingXmls(bool invalidateSettingsCache, out XDocument defaultSettingsXml, out XDocument privateSettingsXml)
        {
            if (invalidateSettingsCache)
            {
                InvalidateSettingsCache();
            }
            if (DefaultSettingsXmlCache == null || PrivateSettingsXmlCache == null)
            {
                defaultSettingsXml = GetDefaultSettingsXml();
                privateSettingsXml = GetPrivateSettingsXml();

                lock (_settingsLock)
                {
                    try
                    {
                        DefaultSettingsXmlCache = defaultSettingsXml;
                        PrivateSettingsXmlCache = privateSettingsXml;
                    }
                    catch (Exception e)
                    {
                        Logger.LogErrorEvent("Failed while getting Settings", e);
                    }
                }
            }

            defaultSettingsXml = DefaultSettingsXmlCache;
            privateSettingsXml = PrivateSettingsXmlCache;

        }

        private static XDocument GetDefaultSettingsXml()
        {
            XDocument defaultSettingsXml;
            string defaultSettingsXmlPath = Path.Combine(Infrastructure.GetDaasInstalationPath(), @"bin\Configuration",
                DefaultSettingsFileName);

            if (!System.IO.File.Exists(defaultSettingsXmlPath))
            {
                var alternatePath = Path.Combine(GetAssemblyDirectory(), "Configuration", DefaultSettingsFileName);
                Logger.LogInfo($"Could not not find the file {defaultSettingsXmlPath}, checking {alternatePath}");
                defaultSettingsXmlPath = alternatePath;

            }

            if (System.IO.File.Exists(defaultSettingsXmlPath))
            {
                using (
                    Stream stream =
                        System.IO.File.OpenRead(defaultSettingsXmlPath)
                    )
                {
                    defaultSettingsXml = XDocument.Load(stream);
                }
            }
            else
            {
                defaultSettingsXml = XDocument.Parse(@"<?xml version=""1.0"" encoding=""utf - 8""?><DiagnosticSettings></DiagnosticSettings>");
            }
            return defaultSettingsXml;
        }

        private static XDocument GetPrivateSettingsXml()
        {
            SetupSettings();
            XDocument privateSettingsXml = null;

            if (Infrastructure.Storage.FileExists(PrivateSettingsFilePath, StorageLocation.UserSiteData))
            {
                using (Stream stream = Infrastructure.Storage.ReadFile(PrivateSettingsFilePath, StorageLocation.UserSiteData))
                {
                    try
                    {
                        privateSettingsXml = XDocument.Load(stream);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarningEvent("Failed while reading the PrivateSettings.xml", ex);
                        SetupSettings(true);
                    }
                }
            }
            return privateSettingsXml;
        }

        private static void SetupSettings(bool overwriteSettings = false)
        {
            if (!Infrastructure.Storage.FileExists(PrivateSettingsFilePath, StorageLocation.UserSiteData) || overwriteSettings)
            {
                // Create the user private settings file
                using (Stream stream = new Settings().GetType().Assembly.GetManifestResourceStream("DaaS.Configuration.PrivateSettings.xml"))
                {
                    try
                    {
                        Infrastructure.Storage.SaveFile(stream, PrivateSettingsFilePath, StorageLocation.UserSiteData);
                        Logger.LogVerboseEvent($"Saved PrivateSettings.xml file successfully with overwriteSettings = {overwriteSettings}");
                    }
                    catch(Exception ex)
                    {
                        Logger.LogErrorEvent("Failed while saving the PrivateSettings.xml", ex);
                    }

                }
            }
        }

        private static List<Diagnoser> LoadDiagnosers(XDocument settingsDoc, Dictionary<String, Collector> collectors, Dictionary<String, Analyzer> analyzers)
        {
            var diagnosers = new List<Diagnoser>();

            var diagnosticSettingsXml = settingsDoc.Element(SettingsXml.DiagnosticSettings);
            if (diagnosticSettingsXml == null)
            {
                return diagnosers;
            }

            var diagnosersXml = diagnosticSettingsXml.Element(SettingsXml.Diagnosers);
            if (diagnosersXml == null)
            {
                return diagnosers;
            }

            foreach (var diagnoserXml in diagnosersXml.Elements(SettingsXml.Diagnoser))
            {
                var diagnoser = new Diagnoser()
                {
                    Name = diagnoserXml.Attribute(SettingsXml.Name).Value
                };

                var descriptionXml = diagnoserXml.Attribute(SettingsXml.Description);
                if (descriptionXml != null)
                {
                    diagnoser.Description = descriptionXml.Value;
                }

                var processCleanupOnExitXml = diagnoserXml.Attribute(SettingsXml.ProcessCleanupOnCancel);
                if (processCleanupOnExitXml != null)
                {
                    diagnoser.ProcessCleanupOnCancel = processCleanupOnExitXml.Value;
                }

                var diagnoserRequiresStorageXml = diagnoserXml.Attribute(SettingsXml.DiagnoserRequiresStorage);
                if (diagnoserRequiresStorageXml != null)
                {
                    if (bool.TryParse(diagnoserRequiresStorageXml.Value, out bool diagnoserRequiresStorage))
                    {
                        diagnoser.DiagnoserRequiresStorage = diagnoserRequiresStorage;
                    }
                }

                var collectorName = diagnoserXml.Element(SettingsXml.Collector).Attribute(SettingsXml.Name).Value;
                Collector collector;
                if (!collectors.TryGetValue(collectorName.ToLower(), out collector))
                {
                    throw new Exception(string.Format("Diagnoser {0} specifies a non-existant collector {1}", diagnoser.Name, collectorName));
                }
                diagnoser.Collector = collector;

                var analyzerName = diagnoserXml.Element(SettingsXml.Analyzer).Attribute(SettingsXml.Name).Value;
                Analyzer analyzer;
                if (!analyzers.TryGetValue(analyzerName.ToLower(), out analyzer))
                {
                    throw new Exception(string.Format("Diagnoser {0} specifies a non-existant analyzer {1}", diagnoser.Name, analyzerName));
                }
                diagnoser.Analyzer = analyzer;

                diagnosers.Add(diagnoser);
            }

            return diagnosers;
        }

        private static void LoadDiagnosticTools(XDocument settingsDoc, ref Dictionary<String, Collector> collectors, ref Dictionary<String, Analyzer> analyzers)
        {
            if (collectors == null)
            {
                collectors = new Dictionary<string, Collector>();
            }
            if (analyzers == null)
            {
                analyzers = new Dictionary<string, Analyzer>();
            }

            var diagnosticSettingsXml = settingsDoc.Element(SettingsXml.DiagnosticSettings);
            if (diagnosticSettingsXml == null)
            {
                return;
            }
            var collectorsXml = diagnosticSettingsXml.Element(SettingsXml.Collectors);
            var analyzersXml = diagnosticSettingsXml.Element(SettingsXml.Analyzers);

            IEnumerable<XElement> tools = new List<XElement>();
            if (collectorsXml != null)
            {
                tools = collectorsXml.Elements();
            }
            if (analyzersXml != null)
            {
                tools = tools.Union(analyzersXml.Elements());
            }

            foreach (var toolXml in tools)
            {
                var toolType = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .FirstOrDefault(
                        t =>
                            t.IsClass &&
                            t.Name.Equals(toolXml.Name.LocalName, StringComparison.OrdinalIgnoreCase));

                if (toolType == null)
                {
                    throw new ArgumentException(string.Format("{0} is not a valid type",
                        toolXml.Name.LocalName));
                }

                var constructor = toolType.GetConstructor(System.Type.EmptyTypes);
                var instance = constructor.Invoke(null);

                foreach (var settingXml in toolXml.Elements())
                {
                    var propertyInfo = toolType.GetProperty(settingXml.Name.LocalName);
                    if (propertyInfo != null)
                    {
                        propertyInfo.SetValue(instance, settingXml.Value, null);
                        //throw new ArgumentException(string.Format("{0} is not a valid setting type",
                        //    settingXml.Name.LocalName));
                    }
                    //propertyInfo.SetValue(instance, settingXml.Value, null);
                }

                foreach (var settingXml in toolXml.Attributes())
                {
                    var propertyInfo = toolType.GetProperty(settingXml.Name.LocalName);
                    if (propertyInfo != null)
                    {
                        propertyInfo.SetValue(instance, settingXml.Value, null);
                        //throw new ArgumentException(string.Format("{0} is not a valid setting type",
                        //    settingXml.Name.LocalName));
                    }
                    //propertyInfo.SetValue(instance, settingXml.Value, null);
                }

                if (typeof(Collector).IsAssignableFrom(toolType))
                {
                    Collector collector = instance as Collector;
                    collectors[collector.Name.ToLower()] = collector;
                }
                else if (typeof (Analyzer).IsAssignableFrom(toolType))
                {
                    Analyzer analyzer = instance as Analyzer;
                    analyzers[analyzer.Name.ToLower()] = analyzer;
                }
                else
                {
                    throw new Exception("Hey, what kind of tool is this?");
                }
            }
        }

        private static string GetAssemblyDirectory()
        {
            string codeBase = Assembly.GetExecutingAssembly().CodeBase;
            UriBuilder uri = new UriBuilder(codeBase);
            string path = Uri.UnescapeDataString(uri.Path);
            return Path.GetDirectoryName(path);
        }
    }
}
