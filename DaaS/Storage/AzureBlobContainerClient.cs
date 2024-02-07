// -----------------------------------------------------------------------
// <copyright file="AzureBlobContainerClient.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO;

namespace DaaS.Storage
{
    internal class AzureBlobContainerClient : IContainerClient
    {
        private readonly BlobContainerClient _blobContainerClient;
        public AzureBlobContainerClient(BlobContainerClient blobContainerClient)
        {
            _blobContainerClient = blobContainerClient;
        }

        public async Task DeleteFileAsync(string filePath)
        {
            await foreach (var blobItem in _blobContainerClient.GetBlobsByHierarchyAsync(prefix: filePath))
            {
                if (blobItem.IsBlob)
                {
                    var blobClient = _blobContainerClient.GetBlobClient(blobItem.Blob.Name);
                    bool deleted = await blobClient.DeleteIfExistsAsync();
                    string message = deleted ? $"Blob '{filePath}' deleted successfully." : $"Blob '{filePath}' does not exist or couldn't be deleted.";
                    Logger.LogVerboseEvent(message);
                }
            }
        }

        public async Task DownloadFileAsync(string sourceFilePath, string destinationFilePath)
        {
            BlobClient blobClient = _blobContainerClient.GetBlobClient(sourceFilePath);
            if (await blobClient.ExistsAsync())
            {
                BlobDownloadInfo blobDownloadInfo = await blobClient.DownloadAsync();
                using (var fileStream = File.OpenWrite(destinationFilePath))
                {
                    await blobDownloadInfo.Content.CopyToAsync(fileStream);
                    fileStream.Close();
                }
            }
            else
            {
                throw new Exception($"Blob '{destinationFilePath}' does not exist in the container.");
            }
        }

        public async Task<IEnumerable<StorageFile>> GetFilesAsync(string directoryPath)
        {
            var files = new List<StorageFile>();
            await foreach (BlobItem blob in _blobContainerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, directoryPath))
            {
                files.Add(new StorageFile
                {
                    Name = Path.GetFileName(blob.Name),
                    FullPath = blob.Name,
                    CreatedOn = blob.Properties.CreatedOn,
                    Size = blob.Properties.ContentLength,
                    LastModified = blob.Properties.LastModified,
                    Uri = new Uri($"{_blobContainerClient.Uri}/{blob.Name}")
                });
            }

            return files;
        }

        public async Task RemoveDirectoryAsync(string directoryPath)
        {
            await foreach (BlobItem blobItem in _blobContainerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, directoryPath))
            {
                BlobClient blobClient = _blobContainerClient.GetBlobClient(blobItem.Name);
                bool deleted = await blobClient.DeleteIfExistsAsync();
                string message = deleted ? $"Blob '{directoryPath}/{blobItem.Name}' deleted successfully." : $"Blob '{directoryPath}/{blobItem.Name}' does not exist or couldn't be deleted.";
                Logger.LogVerboseEvent(message);
            }
        }

        public async Task<Uri> UploadFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken)
        {
            BlobClient blobClient = _blobContainerClient.GetBlobClient(destinationFilePath);
            using (FileStream fs = File.OpenRead(sourceFilePath))
            {
                await blobClient.UploadAsync(fs, true, cancellationToken);
                fs.Close();
            }

            return blobClient.Uri;
        }
    }
}
