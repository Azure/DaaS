// -----------------------------------------------------------------------
// <copyright file="AzureStorageService.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO;

namespace DaaS.Storage
{
    public class AzureStorageService : IStorageService
    {
        const string ContainerName ="memorydumps";

        private readonly ConcurrentDictionary<string, BlobContainerClient> Containers = new ConcurrentDictionary<string, BlobContainerClient>();

        public async Task DeleteFileAsync(string filePath)
        {
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");

            await foreach (var blobItem in containerClient.GetBlobsByHierarchyAsync(prefix: filePath))
            {
                if (blobItem.IsBlob)
                {
                    var blobClient = containerClient.GetBlobClient(blobItem.Blob.Name);
                    await blobClient.DeleteIfExistsAsync();
                }
            }
        }

        public async Task DownloadFileAsync(string sourceFilePath, string destinationFilePath)
        {
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");
            BlobClient blobClient = containerClient.GetBlobClient(sourceFilePath);

            // Check if the blob exists
            if (await blobClient.ExistsAsync())
            {
                // Download the blob to a local file using DownloadTo method
                BlobDownloadInfo blobDownloadInfo = await blobClient.DownloadAsync();
                FileSystemHelpers.CreateDirectoryIfNotExists(Path.GetDirectoryName(destinationFilePath));

                // Save the downloaded content to a local file
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

        public string GetBlobStorageHostName()
        {
            string blobSasUri = Settings.Instance.BlobSasUri;
            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                blobSasUri = Settings.Instance.AccountSasUri;
            }
            try
            {
                if (Uri.TryCreate(blobSasUri, UriKind.Absolute, out Uri uri))
                {
                    return uri.Host;
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Exception while trying to parse Uri for BLOB", ex);
            }

            return string.Empty;
        }

        public async Task<IEnumerable<StorageFile>> GetFilesAsync(string directoryPath)
        {
            directoryPath = directoryPath.ConvertBackSlashesToForwardSlashes();
            var files = new List<StorageFile>();
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");

            await foreach (BlobItem blob in containerClient.GetBlobsAsync(BlobTraits.None, BlobStates.None, directoryPath))
            {
                files.Add(new StorageFile
                {
                    Name = Path.GetFileName(blob.Name),
                    FullPath = blob.Name,
                    CreatedOn = blob.Properties.CreatedOn,
                    Size = blob.Properties.ContentLength,
                    LastModified = blob.Properties.LastModified,
                    Uri = new Uri($"{containerClient.Uri}/{blob.Name}")
                });
            }

            return files;
        }

        public void RemoveDirectory(string directoryPath)
        {
            DeleteFileAsync(directoryPath).Wait();
        }

        public async Task<Uri> UploadFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken)
        {
            destinationFilePath = destinationFilePath.ConvertBackSlashesToForwardSlashes();
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");

            BlobClient blobClient = containerClient.GetBlobClient(destinationFilePath);

            // Open the file and upload it to Azure Storage
            using (FileStream fs = File.OpenRead(sourceFilePath))
            {
                await blobClient.UploadAsync(fs, true, cancellationToken);
                fs.Close();
            }

            return blobClient.Uri;
        }

        public bool ValidateStorageConfiguration(out string storageAccount, out Exception exceptionContactingStorage)
        {
            exceptionContactingStorage = null;
            storageAccount = "";

            try
            {
                string connectionString = Settings.Instance.StorageConnectionString;
                string accountSasUri = Settings.Instance.AccountSasUri;
                BlobContainerClient containerClient = null;

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    containerClient = new BlobContainerClient(connectionString, ContainerName);
                }
                else if (!string.IsNullOrWhiteSpace(accountSasUri))
                {
                    containerClient = new BlobContainerClient(new Uri(accountSasUri));
                }
                else
                {
                    throw new Exception("Azure storage account must be configured");
                }

                storageAccount = containerClient.Uri.Host;

                if (!containerClient.Exists())
                {
                    containerClient.CreateIfNotExists();
                }

                // List blobs in the container
                var blobItems = containerClient.GetBlobsByHierarchy();

                // Enumerate the blobs returned for each item and return true if we can enumerate
                foreach (var blobItem in blobItems)
                {
                    break;
                }

                return true;

            }
            catch (Exception ex)
            {
                exceptionContactingStorage = ex;
                Logger.LogErrorEvent("Encountered exception while validating SAS URI", ex);
            }
            return false;
        }

        private BlobContainerClient GetBlobContainerClient()
        {
            string connectionString = Settings.Instance.StorageConnectionString;
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                if (Containers.TryGetValue(connectionString, out BlobContainerClient container))
                {
                    return container;
                }

                var newContainerClient = new BlobContainerClient(connectionString, ContainerName);
                newContainerClient.CreateIfNotExists();
                Containers.TryAdd(connectionString, newContainerClient);
                return newContainerClient;
            }
            else
            {
                string accountSasUri = Settings.Instance.AccountSasUri;
                if (!string.IsNullOrWhiteSpace(accountSasUri))
                {
                    if (Containers.TryGetValue(accountSasUri, out BlobContainerClient container))
                    {
                        return container;
                    }

                    var newContainerClient = new BlobContainerClient(new Uri(accountSasUri));
                    Containers.TryAdd(accountSasUri, newContainerClient);
                    return newContainerClient;
                }
            }

            return null;
        }
    }
}
