//-----------------------------------------------------------------------
// <copyright file="File.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Diagnostics;
using DaaS.Leases;
using DaaS.Sessions;

namespace DaaS.Storage
{
    public class File
    {
        public string BlobSasUri { get; set; } = string.Empty;
        public virtual string FileName { get; protected set; }

        private string _relativePath = null;
        public string RelativePath
        {
            get
            {
                return _relativePath;
            }
            set
            {
                _relativePath = value.ConvertForwardSlashesToBackSlashes();
            }
        }
        protected internal virtual StorageLocation StorageLocation
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(BlobSasUri))
                {
                    return StorageLocation.BlobStorage;
                }
                else
                {
                    return StorageLocation.UserSiteData;
                }
            }
        }

        public string FullPermanentStoragePath
        {
            get
            {
                if (StorageLocation != StorageLocation.BlobStorage)
                {
                    return Path.Combine(Infrastructure.Settings.GetRootStoragePathForLocation(StorageLocation), RelativePath);
                }

                var path = GetPermanentStoragePathOnBlob(RelativePath, BlobSasUri);

                return path;
            }
        }

        internal static string GetPermanentStoragePathOnBlob(string relativeFilePath, string blobSasUri)
        {
            return BlobController.GetPermanentStoragePathOnBlob(relativeFilePath, blobSasUri);
        }

        internal async virtual Task SaveAsync(Lease lease = null)
        {
            await Infrastructure.Storage.SaveFileAsync(this, StorageLocation, BlobSasUri, lease);
        }

        internal virtual void Save(Lease lease = null)
        {
            Infrastructure.Storage.SaveFile(this, StorageLocation, BlobSasUri, lease);
        }

        internal virtual async Task<Stream> GetFileStreamAsync()
        {
            if (await Infrastructure.Storage.FileExistsAsync(RelativePath, StorageLocation.TempStorage, BlobSasUri))
            {
                return await Infrastructure.Storage.ReadFileAsync(RelativePath, StorageLocation.TempStorage, string.Empty);
            }
            return await Infrastructure.Storage.ReadFileAsync(RelativePath, StorageLocation, BlobSasUri);
        }

        internal virtual Stream GetFileStream()
        {
            if (Infrastructure.Storage.FileExists(RelativePath, StorageLocation.TempStorage, string.Empty))
            {
                return Infrastructure.Storage.ReadFile(RelativePath, StorageLocation.TempStorage, string.Empty);
            }
            return Infrastructure.Storage.ReadFile(RelativePath, StorageLocation, BlobSasUri);
        }
    }

    public class Report : File
    {
        private Report() { }

        internal static Report GetReport(string relativePath)
        {
            // Normalize the slashes
            relativePath = relativePath.ConvertBackSlashesToForwardSlashes();

            var report = new Report()
            {
                RelativePath = relativePath,
                FileName = Path.GetFileName(relativePath),
                BlobSasUri = string.Empty
            };

            return report;
        }

        public override bool Equals(object obj)
        {
            Report report = obj as Report;
            if (report == null)
            {
                return false;
            }

            return RelativePath.Equals(report.RelativePath, StringComparison.InvariantCultureIgnoreCase);
        }

        public override int GetHashCode()
        {
            return RelativePath.GetHashCode();
        }

        protected internal override StorageLocation StorageLocation
        {
            get
            {
                return StorageLocation.UserSiteData;
            }
        }
    }

    public class Log : File
    {
        public DateTime StartTime { get; private set; }
        public DateTime EndTime { get; private set; }
        private Collector Collector { get; set; }
        public DateTime AnalysisStarted { get; set; } = DateTime.MinValue.ToUniversalTime();
        public string InstanceAnalyzing { get; set; } = "";
        public double FileSize { get; set; } = 0;

        private Log(DateTime startTime, DateTime endTime, Collector collector, double fileSize, string blobSasUri)
        {
            StartTime = startTime;
            EndTime = endTime;
            Collector = collector;
            FileSize = fileSize;
            BlobSasUri = blobSasUri;
        }

        internal static Log GetLog(DateTime startTime, DateTime endTime, string relativePath, Collector collector, double fileSize, string blobSasUri, string defaultHostName)
        {
            // Create a log object reflecting that new location & name
            var log = new Log(startTime, endTime, collector, fileSize, blobSasUri);
            var logDestinationDir = log.GetRelativeDirectory(defaultHostName);
            log.FileName = log.GetPermanentFileName(Path.GetFileName(relativePath));
            log.RelativePath = Path.Combine(logDestinationDir, log.FileName);

            return log;
        }

        private string GetRelativeDirectory(string defaultHostName)
        {
            var path = GetRelativeDirectory(StartTime, EndTime, Collector, defaultHostName);
            return path;
        }

        internal static string GetRelativeDirectory(DateTime startTime, DateTime endTime, Collector collector, string defaultHostName)
        {
            var path = Path.Combine(
                "Logs",
                defaultHostName,
                startTime.ToString(SessionConstants.SessionFileNameFormat),
                Settings.InstanceName,
                collector.Name);
            return path;
        }

        public override bool Equals(object obj)
        {
            Log otherLog = obj as Log;
            if (otherLog == null)
            {
                return false;
            }

            return RelativePath.Equals(otherLog.RelativePath, StringComparison.InvariantCultureIgnoreCase);
        }

        public override int GetHashCode()
        {
            return RelativePath.GetHashCode();
        }

        private string GetPermanentFileName(string baseName)
        {
            return baseName;
        }

        internal async Task CacheLogInTempFolderAsync()
        {
            if (await Infrastructure.Storage.FileExistsAsync(RelativePath, StorageLocation.TempStorage))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(BlobSasUri))
            {
                await Infrastructure.Storage.DownloadFileFromBlobAsync(RelativePath, StorageLocation.TempStorage, BlobSasUri);
            }
            else
            {
                using (Stream fileStream = await GetFileStreamAsync())
                {
                    Infrastructure.Storage.SaveFile(fileStream, RelativePath, StorageLocation.TempStorage);
                }
            }
            
        }

        internal static Log GetLogFromPermanentStorage(string relativeStoragePath, double fileSize, string blobSasUri, DateTime startTime, DateTime endTime)
        {
            // Normalize the slashes
            relativeStoragePath = relativeStoragePath.ConvertBackSlashesToForwardSlashes();
            var pathComponents = relativeStoragePath.Split('/');
            var collectorName = pathComponents[4];
            Collector collector =
                Infrastructure.Settings.GetDiagnosers()
                    .Where(d => d.Collector.Name.Equals(collectorName))
                    .Select(d => d.Collector)
                    .FirstOrDefault();

            if (collector == null)
            {
                // The settings have been changed and the collector has been removed. Lets not crash over this
                collector = new RangeCollector()
                {
                    Name = collectorName
                };
            }

            Log log = new Log(startTime, endTime, collector, fileSize, blobSasUri);
            log.RelativePath = relativeStoragePath;
            log.FileName = Path.GetFileName(log.RelativePath);
            return log;
        }

        internal string GetInstanceName()
        {
            var relativeStoragePath = this.RelativePath.Replace('\\', '/');
            var pathComponents = relativeStoragePath.Split('/');
            var instanceName = pathComponents[3];
            return instanceName;
        }
    }
}
