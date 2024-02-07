// -----------------------------------------------------------------------
// <copyright file="LegacyBlobContainerClient.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Blob;

namespace DaaS.Storage
{
    internal class LegacyBlobContainerClient : IContainerClient
    {
        private readonly CloudBlobContainer _cloudBlobContainer;
        public LegacyBlobContainerClient(CloudBlobContainer cloudBlobContainer)
        {
            _cloudBlobContainer = cloudBlobContainer;
        }

        public async Task DeleteFileAsync(string filePath)
        {
            CloudBlockBlob blob = _cloudBlobContainer.GetBlockBlobReference(filePath);
            bool deleted = await blob.DeleteIfExistsAsync();

            string message = deleted ? $"Blob '{filePath}' deleted successfully." : $"Blob '{filePath}' does not exist or couldn't be deleted.";
            Logger.LogVerboseEvent(message);
        }

        public async Task DownloadFileAsync(string sourceFilePath, string destinationFilePath)
        {
            CloudBlockBlob blob = _cloudBlobContainer.GetBlockBlobReference(sourceFilePath);
            if (await blob.ExistsAsync())
            {
                await blob.DownloadToFileAsync(destinationFilePath, FileMode.Create);
            }
            else
            {
                throw new Exception($"Blob '{destinationFilePath}' does not exist in the container.");
            }
        }

        public async Task<IEnumerable<StorageFile>> GetFilesAsync(string directoryPath)
        {
            BlobContinuationToken continuationToken = null;
            var files = new List<StorageFile>();
            do
            {
                var resultSegment = await _cloudBlobContainer.ListBlobsSegmentedAsync(directoryPath, true, BlobListingDetails.None, new int?(), continuationToken, null, null);
                continuationToken = resultSegment.ContinuationToken;

                foreach (IListBlobItem item in resultSegment.Results)
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob blob = (CloudBlockBlob)item;
                        files.Add(new StorageFile
                        {
                            Name = Path.GetFileName(blob.Name),
                            FullPath = blob.Name,
                            CreatedOn = blob.Properties.Created,
                            Size = blob.Properties.Length,
                            LastModified = blob.Properties.LastModified,
                            Uri = new Uri($"{_cloudBlobContainer.Uri}/{blob.Name}")
                        });
                    }
                }
            } while (continuationToken != null);

            return files;
        }

        public async Task RemoveDirectoryAsync(string directoryPath)
        {
            CloudBlobDirectory directory = _cloudBlobContainer.GetDirectoryReference(directoryPath);
            BlobContinuationToken continuationToken = null;
            do
            {
                var resultSegment = await directory.ListBlobsSegmentedAsync(continuationToken);
                continuationToken = resultSegment.ContinuationToken;

                foreach (IListBlobItem item in resultSegment.Results)
                {
                    if (item.GetType() == typeof(CloudBlockBlob))
                    {
                        CloudBlockBlob blob = (CloudBlockBlob)item;

                        bool deleted = await blob.DeleteIfExistsAsync();

                        string message = deleted ? $"Blob '{directoryPath}/{blob.Name}' deleted successfully." : $"Blob '{directoryPath}/{blob.Name}' does not exist or couldn't be deleted.";
                        Logger.LogVerboseEvent(message);
                    }
                }
            } while (continuationToken != null);
        }

        public async Task<Uri> UploadFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken)
        {
            await _cloudBlobContainer.CreateIfNotExistsAsync();
            CloudBlockBlob blob = _cloudBlobContainer.GetBlockBlobReference(destinationFilePath);
            await blob.UploadFromFileAsync(sourceFilePath, cancellationToken);
            return blob.Uri;
        }
    }
}
