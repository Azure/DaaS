// -----------------------------------------------------------------------
// <copyright file="StorageController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Leases;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace DaaS.Storage
{
    internal interface IStorageController
    {
        Task SaveFileAsync(File file, StorageLocation location, string blobSasUri = "", Lease lease = null);
        void SaveFile(File file, StorageLocation location, string blobSasUri = "", Lease lease = null);
        void SaveFile(Stream fileContents, string relativeFilePath, StorageLocation location, string blobSasUri = "", Lease lease = null);
        Task<Stream> ReadFileAsync(string filePath, StorageLocation location, string blobSasUri = "");
        Stream ReadFile(string filePath, StorageLocation location, string blobSasUri = "");
        List<string> GetFilesInDirectory(string directoryPath, StorageLocation location, string blobSasUri, string searchPattern = "*", SearchOption searchOption = SearchOption.AllDirectories);
        Task<bool> FileExistsAsync(string filePath, StorageLocation location, string blobSasUri = "");
        bool FileExists(string filePath, StorageLocation location, string blobSasUri = "");
        Task DeleteFileAsync(File file, string blobSasUri = "", Lease lease = null);
        Task DeleteFileAsync(string filePath, string blobSasUri = "",  Lease lease = null);
        Task MoveFileAsync(File file, string newPath, StorageLocation location, string blobSasUri = "");
        Task MoveFileAsync(string oldPath, string newPath, StorageLocation location, string blobSasUri = "");
        void MoveFile(File file, string newPath, StorageLocation location, string blobSasUri = "");
        void MoveFile(string oldPath, string newPath, StorageLocation location, string blobSasUri = "");
        void CopyFileToLocation(File file, StorageLocation destinationLocation, string blobSasUri = "");
        string GetNewTempFolder(string folderName);
        double GetFileSize(string directoryPath, string fileName, StorageLocation location, string blobSasUri = "");
        void RemoveAllFilesInDirectory(string directoryPath, StorageLocation location, string blobSasUri = "");
        Task DownloadFileFromBlobAsync(string relativePath, StorageLocation location, string blobSasUri);
        Task UploadFileToBlobAsync(string relativePath, StorageLocation location, string blobSasUri);
    }

    public enum StorageLocation
    {
        TempStorage,
        UserSiteData,
        BlobStorage,
        UserSiteRoot
    }

    internal class StorageController : IStorageController
    {
        private static StorageController _instance;
        public static StorageController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StorageController();
                }
                return _instance;
            }
            set { _instance = value; }
        }

        private StorageController() { }

        public async Task SaveFileAsync(File file, StorageLocation location, string blobSasUri, Lease lease = null)
        {
            using (Stream contentStream = await file.GetFileStreamAsync())
            {
                if (string.IsNullOrEmpty(file.RelativePath))
                {
                    throw new Exception("Need to set a relative path for the file");
                }

                await SaveFileAsync(contentStream, file.RelativePath, location, blobSasUri, lease);
            }
        }

        private async Task SaveFileAsync(Stream fileContents, string relativeFilePath, StorageLocation location, string blobSasUri, Lease lease = null)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                var blockBlob = BlobController.GetBlobForFile(relativeFilePath, blobSasUri);
                await blockBlob.UploadFromStreamAsync(fileContents, GetAccessCondition(lease), null, null);
                return;
            }

            var rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
            var fullPath = Path.Combine(rootPath, relativeFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using (FileStream file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                await fileContents.CopyToAsync(file);
            }
        }

        public void SaveFile(File file, StorageLocation location, string blobSasUri, Lease lease = null)
        {
            using (Stream contentStream = file.GetFileStream())
            {
                if (string.IsNullOrEmpty(file.RelativePath))
                {
                    throw new Exception("Need to set a relative path for the file");
                }

                SaveFile(contentStream, file.RelativePath, location, blobSasUri, lease);
            }
        }

        public void SaveFile(Stream fileContents, string relativeFilePath, StorageLocation location, string blobSasUri, Lease lease = null)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                var blockBlob = BlobController.GetBlobForFile(relativeFilePath, blobSasUri);
                blockBlob.UploadFromStream(fileContents, GetAccessCondition(lease), null, null);
                return;
            }

            var rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
            var fullPath = Path.Combine(rootPath, relativeFilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            using (FileStream file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                fileContents.CopyTo(file);
            }
        }

        public async Task<Stream> ReadFileAsync(string filePath, StorageLocation location, string blobSasUri)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                var blob = BlobController.GetBlobForFile(filePath, blobSasUri);
                var stream = new MemoryStream();
                await blob.DownloadToStreamAsync(stream);
                stream.Position = 0;
                return stream;
            }

            string baseDir = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
            string fullPath = Path.Combine(baseDir, filePath);
            FileStream fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return fileStream;
        }

        public Stream ReadFile(string filePath, StorageLocation location, string blobSasUri)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                var blob = BlobController.GetBlobForFile(filePath, blobSasUri);
                var stream = new MemoryStream();
                blob.DownloadToStream(stream);
                stream.Position = 0;
                return stream;
            }

            string baseDir = GetRootStoragePathForWhenBlobStorageIsNotConfigured((StorageLocation)location);
            string fullPath = Path.Combine(baseDir, filePath);
            FileStream fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return fileStream;
        }

        public List<string> GetFilesInDirectory(string directoryPath, StorageLocation location, string blobSasUri, string searchPattern = "*", SearchOption searchOption = SearchOption.AllDirectories)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                List<string> files = new List<string>();
                var dir = BlobController.GetBlobDirectory(directoryPath, blobSasUri);
                foreach (
                    IListBlobItem item in
                        dir.ListBlobs(useFlatBlobListing: true))
                {
                    var relativePath = item.Uri.ToString().Replace(item.Container.Uri.ToString() + "/", "");
                    files.Add(relativePath);
                }
                return files;
            }
            else
            {
                string rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
                string fullDirPath = Path.Combine(rootPath, directoryPath).ConvertForwardSlashesToBackSlashes();

                var files = new List<string>();
                if (Directory.Exists(fullDirPath))
                {
                    files = Directory.GetFiles(fullDirPath, searchPattern, searchOption).ToList();
                }
                return files;
            }
        }

        public double GetFileSize(string directoryPath, string fileName, StorageLocation location, string blobSasUri)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                return 0;
            }
            else
            {
                double fileSize = 0;
                string rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
                string fullFileName = Path.Combine(rootPath, directoryPath, fileName).ConvertForwardSlashesToBackSlashes();
                if (System.IO.File.Exists(fullFileName))
                {
                    try
                    {
                        fileSize = new FileInfo(fullFileName).Length;
                    }
                    catch (Exception)
                    {
                    }
                }
                return fileSize;
            }
        }

        public async Task<bool> FileExistsAsync(string filePath, StorageLocation location, string blobSasUri)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                var blob = BlobController.GetBlobForFile(filePath, blobSasUri);
                return await blob.ExistsAsync();
            }

            return FileExistsOnDisk(filePath, location);
        }

        public bool FileExists(string filePath, StorageLocation location, string blobSasUri)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                var blob = BlobController.GetBlobForFile(filePath, blobSasUri);
                return blob.Exists();
            }

            return FileExistsOnDisk(filePath, location);
        }

        private bool FileExistsOnDisk(string filePath, StorageLocation location)
        {
            string rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
            string fullPath = Path.Combine(rootPath, filePath);
            return System.IO.File.Exists(fullPath);
        }

        /// <summary>
        /// Delete the given file from all known locations
        /// </summary>
        public async Task DeleteFileAsync(File file, string blobSasUri, Lease lease = null)
        {
            await DeleteFileAsync(file.RelativePath, blobSasUri);
        }

        /// <summary>
        /// Delete the given file from all known locations
        /// </summary>
        public async Task DeleteFileAsync(string filePath, string blobSasUri, Lease lease = null)
        {
            if (!string.IsNullOrWhiteSpace(blobSasUri))
            {
                var fileBlob = BlobController.GetBlobForFile(filePath, blobSasUri);
                await fileBlob.DeleteIfExistsAsync(DeleteSnapshotsOption.None, GetAccessCondition(lease), null, null);
            }

            foreach (var baseDir in new string[] { Infrastructure.Settings.TempDir, Settings.UserSiteStorageDirectory })
            {
                string fullPath = Path.Combine(baseDir, filePath);
                if (System.IO.File.Exists(fullPath))
                {
                    RetryHelper.RetryOnException("Deleting file asynchronously...", () =>
                    {
                        System.IO.File.Delete(fullPath);
                    }, TimeSpan.FromSeconds(1));
                }
            }
        }

        public async Task MoveFileAsync(File file, string newPath, StorageLocation location, string blobSasUri)
        {
            await MoveFileAsync(file.RelativePath, newPath, location, blobSasUri);
            file.RelativePath = newPath;
        }

        public async Task MoveFileAsync(string oldPath, string newPath, StorageLocation location, string blobSasUri)
        {
            if (oldPath.StartsWith("\\") || oldPath.StartsWith("/"))
            {
                oldPath = oldPath.Remove(0, 1);
            }
            if (Path.GetFullPath(oldPath).Equals(Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            {
                // Moving a file from and to the same path. Don't do anything
                return;
            }

            if (UseBlobStorage(location, blobSasUri))
            {
                var oldBlob = BlobController.GetBlobForFile(oldPath, blobSasUri);
                var newBlob = BlobController.GetBlobForFile(newPath, blobSasUri);
                await newBlob.StartCopyAsync(oldBlob);
                //await newBlob.StartCopyFromBlobAsync(oldBlob);
                await oldBlob.DeleteIfExistsAsync();
            }
            else
            {
                MoveFileOnDisk(oldPath, newPath, location);
            }
        }

        public void MoveFile(File file, string newPath, StorageLocation location, string blobSasUri)
        {
            MoveFile(file.RelativePath, newPath, location, blobSasUri);
            file.RelativePath = newPath;
        }

        public void MoveFile(string oldPath, string newPath, StorageLocation location, string blobSasUri)
        {
            if (oldPath.StartsWith("\\") || oldPath.StartsWith("/"))
            {
                oldPath = oldPath.Remove(0, 1);
            }
            if (Path.GetFullPath(oldPath).Equals(Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            {
                // Moving a file from and to the same path. Don't do anything
                return;
            }

            if (UseBlobStorage(location, blobSasUri))
            {
                var oldBlob = BlobController.GetBlobForFile(oldPath, blobSasUri);
                var newBlob = BlobController.GetBlobForFile(newPath, blobSasUri);
                newBlob.StartCopy(oldBlob);
                //newBlob.StartCopyFromBlob(oldBlob);
                oldBlob.DeleteIfExists();
            }
            else
            {
                MoveFileOnDisk(oldPath, newPath, location);
            }
        }

        private void MoveFileOnDisk(string oldPath, string newPath, StorageLocation location)
        {
            string baseDir = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
            string oldFullPath = Path.Combine(baseDir, oldPath);
            string newFullPath = Path.Combine(baseDir, newPath);
            if (oldFullPath.Equals(newFullPath, StringComparison.OrdinalIgnoreCase))
            {
                // Moving file from and to the same location. Don't do anything
                return;
            }
            Logger.LogDiagnostic("Moving file from {0} to {1}", oldFullPath, newFullPath);
            Directory.CreateDirectory(Path.GetDirectoryName(newFullPath));
            if (!System.IO.File.Exists(oldFullPath))
            {
                // Source file doesn't exist
                throw new FileNotFoundException(oldPath);
            }
            if (System.IO.File.Exists(newFullPath))
            {
                // File.Move doesn't work if the destination file already exists
                Logger.LogDiagnostic("Deleting preexisting file {0}", newFullPath);
                RetryHelper.RetryOnException("Deleting preexisting...", () =>
                {
                    System.IO.File.Delete(newFullPath);
                }, TimeSpan.FromSeconds(1));
            }
            System.IO.File.Move(oldFullPath, newFullPath);
            if (System.IO.File.Exists(oldFullPath))
            {
                // If the source file is in use File.Move doesn't delete it
                Logger.LogDiagnostic("The source file wasn't deleted (it may have been in use). Let's delete it now");
                RetryHelper.RetryOnException("Deleting old path while moving files on disk...", () =>
                {
                    System.IO.File.Delete(oldFullPath);
                }, TimeSpan.FromSeconds(1));
            }
        }

        public void CopyFileToLocation(File file, StorageLocation destinationLocation, string blobSasUri)
        {
            if (FileExists(file.RelativePath, destinationLocation, blobSasUri))
            {
                Logger.LogDiagnostic("The file {0} is already at the destination {1}", file.RelativePath, destinationLocation);
                return;
            }

            using (var stream = file.GetFileStream())
            {
                SaveFile(stream, file.RelativePath, destinationLocation, blobSasUri);
            }
        }

        public string GetNewTempFolder(string folderName)
        {
            var tempDir = Path.Combine(Infrastructure.Settings.TempDir, folderName);
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }
            return tempDir;
        }

        private string GetRootStoragePathForWhenBlobStorageIsNotConfigured(StorageLocation location)
        {
            string rootPath;
            switch (location)
            {
                case StorageLocation.BlobStorage:
                    throw new Exception("Blob storage path was requested, but blob storage is not configured");
                    break;
                case StorageLocation.TempStorage:
                    rootPath = Infrastructure.Settings.TempDir;
                    break;
                case StorageLocation.UserSiteData:
                    rootPath = Settings.UserSiteStorageDirectory;
                    break;
                case StorageLocation.UserSiteRoot:
                    rootPath = Settings.SiteRootDir;
                    break;
                default:
                    throw new IndexOutOfRangeException("Invalid value specified for location");
            }
            return rootPath;
        }

        private bool UseBlobStorage(StorageLocation location, string blobSasUri)
        {
            return location == StorageLocation.BlobStorage && !string.IsNullOrWhiteSpace(blobSasUri);
        }

        private static AccessCondition GetAccessCondition(Lease lease)
        {
            AccessCondition accessCondition = null;
            if (lease != null)
            {
                accessCondition = AccessCondition.GenerateLeaseCondition(lease.Id);
            }
            return accessCondition;
        }

        public void RemoveAllFilesInDirectory(string directoryPath, StorageLocation location, string blobSasUri)
        {
            if (UseBlobStorage(location, blobSasUri))
            {
                return;
            }

            string baseDir = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
            string fullPath = Path.Combine(baseDir, directoryPath);
            if (System.IO.File.Exists(fullPath))
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(fullPath);
            }

        }

        public async Task DownloadFileFromBlobAsync(string relativePath, StorageLocation location, string blobSasUri)
        {
            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                throw new InvalidOperationException("BlobSasUri cannot be empty");
            }
            try
            {
                var rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
                var fullPath = Path.Combine(rootPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                var blob = BlobController.GetBlobForFile(relativePath, blobSasUri);
                await blob.DownloadToFileAsync(fullPath, FileMode.Append);
                Logger.LogVerboseEvent($"Downloaded {relativePath} from blob storage");
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent($"DownloadFileFromBlob - Failed while downloading file {relativePath} from blob", ex);
            }
        }

        public async Task UploadFileToBlobAsync(string relativePath, StorageLocation location, string blobSasUri)
        {
            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                throw new InvalidOperationException("BlobSasUri cannot be empty");
            }
            try
            {
                var rootPath = GetRootStoragePathForWhenBlobStorageIsNotConfigured(location);
                var fullPath = Path.Combine(rootPath, relativePath);
                var blob = BlobController.GetBlobForFile(relativePath, blobSasUri);
                await blob.UploadFromFileAsync(fullPath);
                Logger.LogVerboseEvent($"Uploaded {relativePath} to blob storage");
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent($"UploadFileToBlobAsync - Failed while uploading file {relativePath} at {location} to blob", ex);
            }
        }
    }
}
