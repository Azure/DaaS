// -----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Newtonsoft.Json;

namespace DaaS.Configuration
{
    public class Settings
    {
        public const string AlertQueueName = "diagnosticalerts";

        const string DefaultSettingsFileName = "DiagnosticSettings.json";
        const string PrivateSettingsFilePath = "PrivateSettings.json";
        const string DefaultHostNameSandboxProperty = "SANDBOX_FUNCTION_RESOURCE_ID";
        const string DaasStorageSasUri = "WEBSITE_DAAS_STORAGE_SASURI";
        const string DaasStorageConnectionString = "WEBSITE_DAAS_STORAGE_CONNECTIONSTRING";
        const string SandboxPropertyStorageAccountResourceId = "WEBSITE_DAAS_STORAGE_RESOURCEID";
        const string ContainerName = "memorydumps";

        private string _diagnosticToolsPath;
        private bool? _isSandboxAvailable;
        private string _siteName;
        private static readonly ConcurrentDictionary<string, StorageAccountConfiguration> _blobStorageConnectionStrings = new ConcurrentDictionary<string, StorageAccountConfiguration>();

        private static string _instanceName;
        private string _tempDir;
        private static string _siteRootDir;

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
        public int LeaseDurationInSeconds { get; set; }
        public int HeartBeatLifeTimeInSeconds { get; set; }
        public Diagnoser[] Diagnosers { get; set; }
        public string DiagnosticToolsPath { get; set; }

        public bool IsStorageAccountConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AccountSasUri) || !string.IsNullOrWhiteSpace(StorageConnectionString);
            }
        }

        internal string BlobSasUri
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(StorageConnectionString))
                {
                    return GetBlobSasUri(StorageConnectionString);
                }

                return string.Empty;
            }
        }

        internal string AccountSasUri
        {
            get
            {
                if (IsSandBoxAvailable())
                {
                    string accountSasUri = GetSandboxProperty(DaasStorageSasUri);
                    if (!string.IsNullOrWhiteSpace(accountSasUri))
                    {
                        return accountSasUri;
                    }
                }

                string envvar = Environment.GetEnvironmentVariable(DaasStorageSasUri);
                if (!string.IsNullOrWhiteSpace(envvar))
                {
                    return envvar;
                }

                return string.Empty;
            }
        }

        internal string AccountResourceId
        {
            get
            {
                if (IsSandBoxAvailable())
                {
                    string resourceId = GetSandboxProperty(SandboxPropertyStorageAccountResourceId);
                    if (!string.IsNullOrWhiteSpace(resourceId))
                    {
                        return resourceId;
                    }
                }

                string envvar = Environment.GetEnvironmentVariable(SandboxPropertyStorageAccountResourceId);
                if (!string.IsNullOrWhiteSpace(envvar))
                {
                    return envvar;
                }

                return string.Empty;
            }
        }

        internal string StorageConnectionString
        {
            get
            {
                if (IsSandBoxAvailable())
                {
                    string storageConnectionString = GetSandboxProperty(DaasStorageConnectionString);
                    if (!string.IsNullOrWhiteSpace(storageConnectionString))
                    {
                        return storageConnectionString;
                    }
                }

                string envvar = Environment.GetEnvironmentVariable(DaasStorageConnectionString);
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
        internal string DefaultHostName
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
        internal string DefaultScmHostName
        {
            get
            {
                if (!IsSandBoxAvailable())
                {
                    return SiteName;
                }

                return GetSandboxProperty(DefaultHostNameSandboxProperty);
            }
        }

        internal string SiteName
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

        internal string UserSiteStorageDirectory
        {
            get
            {
                return Path.Combine(SiteRootDir, @"data\DaaS");
            }
        }

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

        internal string TempDir
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_tempDir))
                {
                    _tempDir = Environment.GetEnvironmentVariable("TEMP");
                }
                return _tempDir;
            }
            set
            {
                _tempDir = value;
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

        internal TimeSpan LeaseRenewalTime
        {
            get
            {
                return TimeSpan.FromMilliseconds(LeaseDuration.TotalMilliseconds / 2);
            }
        }

        internal TimeSpan LeaseDuration
        {
            get
            {
                return TimeSpan.FromSeconds(LeaseDurationInSeconds);
            }
        }

        internal string InstanceName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_instanceName))
                {
                    _instanceName = Environment.GetEnvironmentVariable("COMPUTERNAME");
                }

                return _instanceName;
            }
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

        private static Settings CreateSettingsInstance()
        {
            string defaultSettingsStream;
            using (Stream stream = typeof(Settings).Assembly.GetManifestResourceStream("DaaS.Configuration.DiagnosticSettings.json"))
            {
                using StreamReader reader = new StreamReader(stream);
                defaultSettingsStream = reader.ReadToEnd();
            }

            var defaultSettings = JsonConvert.DeserializeObject<Settings>(defaultSettingsStream);
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
            bool privateSettingsParsed = false;
            try
            {
                var settingsPath = CreatePrivateSettingsFileIfNotExists();
                if (!string.IsNullOrWhiteSpace(settingsPath))
                {
                    var privateSettings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath));
                    privateSettingsParsed = true;
                    return privateSettings;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while parsing private settings file, trying to overwrite the existing file", ex);
            }

            if (!privateSettingsParsed)
            {
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
            try
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
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while creating private settings file", ex);
            }

            return string.Empty;
        }

        private static string GetSandboxProperty(string propertyName)
        {
            int copiedBytes = 0;
            byte[] valueBuffer = new byte[4096];
            if (GetSandboxProperty(propertyName, valueBuffer, valueBuffer.Length, 0, ref copiedBytes))
            {
                string value = Encoding.Unicode.GetString(valueBuffer, 0, copiedBytes);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private string GetBlobSasUri(string connectionString)
        {
            if (_blobStorageConnectionStrings.ContainsKey(connectionString))
            {
                if (DateTime.UtcNow.Subtract(_blobStorageConnectionStrings[connectionString].DateAdded.UtcDateTime).TotalDays < 28)
                {
                    return _blobStorageConnectionStrings[connectionString].SasUri;
                }
            }

            string sasUri = GenerateBlobSasUri(connectionString);
            Logger.LogVerboseEvent($"Generated new SAS URI for connectionString");

            var dictValue = new StorageAccountConfiguration()
            {
                DateAdded = new DateTimeOffset(DateTime.UtcNow),
                SasUri = sasUri
            };

            if (_blobStorageConnectionStrings.TryAdd(connectionString, dictValue))
            {
                Logger.LogVerboseEvent($"Updated cache for SAS URI for connectionString at {_blobStorageConnectionStrings[connectionString].DateAdded}");
            }

            return sasUri;
        }

        //private static string GenerateBlobSasUri(string connectionString)
        //{
        //    BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);

        //    var accountSasBuilder = new AccountSasBuilder
        //    {
        //        ResourceTypes = AccountSasResourceTypes.All,
        //        Services = AccountSasServices.Blobs,
        //        Protocol = SasProtocol.Https,
        //        ExpiresOn = DateTimeOffset.UtcNow.AddMonths(1),
        //    };

        //    var matches = Regex.Matches(connectionString, @"([^=]+)=([^\;]+);?");
        //    var parts = matches.Cast<Match>().ToDictionary(value => value.Groups[1].ToString(), value => value.Groups[2].ToString());

        //    if (!parts.ContainsKey("AccountKey") || !parts.ContainsKey("AccountName"))
        //    {
        //        throw new Exception("ConnectionString is not properly formed");
        //    }

        //    var accountKey = parts["AccountKey"];
        //    var accountName = parts["AccountName"];

        //    accountSasBuilder.SetPermissions(AccountSasPermissions.All);

        //    var storageSharedKeyCredential = new StorageSharedKeyCredential(accountName, accountKey);

        //    string sasToken = accountSasBuilder.ToSasQueryParameters(storageSharedKeyCredential).ToString();

        //    return blobServiceClient.Uri + ContainerName + "?" + sasToken;
        //}

        private static string GenerateBlobSasUri(string connectionString)
        {
            var blobContainerClient = new BlobContainerClient(connectionString, ContainerName);
            var blobSasBuilder = new BlobSasBuilder()
            {
                BlobContainerName = ContainerName,
                ExpiresOn = DateTime.UtcNow.AddMonths(1)
            };

            blobSasBuilder.SetPermissions(BlobAccountSasPermissions.All);
            return blobContainerClient.GenerateSasUri(blobSasBuilder).ToString();
        }
    }
}
