﻿// -----------------------------------------------------------------------
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
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;

namespace DaaS.Storage
{
    public class AzureStorageService : IStorageService
    {
        const string ContainerName = "memorydumps";

        private readonly ConcurrentDictionary<string, IContainerClient> Containers = new ConcurrentDictionary<string, IContainerClient>();
        private readonly string _connectionString = "";
        private readonly string _accountSasUri = "";

        public AzureStorageService()
        {
        }

        /// <summary>
        /// Should be called only from Unit Tests
        /// </summary>
        /// <param name="connectionString"></param>
        /// <param name="accountSasUri"></param>
        public AzureStorageService(string connectionString, string accountSasUri)
        {
            this._connectionString = connectionString;
            this._accountSasUri = accountSasUri;
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

        public bool ValidateStorageConfiguration(out string storageAccount, out Exception exceptionContactingStorage)
        {
            exceptionContactingStorage = null;
            storageAccount = "";

            try
            {
                string connectionString = Settings.Instance.StorageConnectionString;
                string accountSasUri = Settings.Instance.AccountSasUri;
                BlobContainerClient containerClient = null;

                bool usingConnectionString = false;
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    containerClient = new BlobContainerClient(connectionString, ContainerName);
                    usingConnectionString = true;
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

                Logger.LogVerboseEvent($"Storage account validated succesfully {(usingConnectionString ? "using connection string." : "using SAS URI.")}");
                return true;

            }
            catch (Exception ex)
            {
                exceptionContactingStorage = ex;
                Logger.LogErrorEvent("Encountered exception while validating SAS URI", ex);
            }
            return false;
        }

        private IContainerClient GetBlobContainerClient()
        {
            var connectionString = GetConnectionString();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                if (Containers.TryGetValue(connectionString, out IContainerClient containerClient))
                {
                    return containerClient;
                }

                var newContainerClient = new BlobContainerClient(connectionString, ContainerName);
                newContainerClient.CreateIfNotExists();
                var azureBlobContainerClient = new AzureBlobContainerClient(newContainerClient);
                Containers.TryAdd(connectionString, azureBlobContainerClient);
                return azureBlobContainerClient;
            }
            else
            {
                var accountSasUri = GetAccountSasUri();
                if (!string.IsNullOrWhiteSpace(accountSasUri))
                {
                    if (Containers.TryGetValue(accountSasUri, out IContainerClient containerClient))
                    {
                        return containerClient;
                    }

                    var newContainerClient = new CloudBlobContainer(new Uri(accountSasUri));
                    newContainerClient.CreateIfNotExists();
                    var legacyBlobContainerClient = new LegacyBlobContainerClient(newContainerClient);
                    Containers.TryAdd(accountSasUri, legacyBlobContainerClient);
                    return legacyBlobContainerClient;
                }
            }

            return null;
        }

        private string GetAccountSasUri()
        {
            return string.IsNullOrWhiteSpace(_accountSasUri) ? Settings.Instance.AccountSasUri : _accountSasUri;
        }

        private string GetConnectionString()
        {
            return string.IsNullOrWhiteSpace(_connectionString) ? Settings.Instance.StorageConnectionString : _connectionString;
        }

        public async Task DeleteFileAsync(string filePath)
        {
            filePath = filePath.ConvertBackSlashesToForwardSlashes();
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");
            await containerClient.DeleteFileAsync(filePath);
        }

        public async Task DownloadFileAsync(string sourceFilePath, string destinationFilePath)
        {
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");
            FileSystemHelpers.CreateDirectoryIfNotExists(Path.GetDirectoryName(destinationFilePath));
            sourceFilePath = sourceFilePath.ConvertBackSlashesToForwardSlashes();
            await containerClient.DownloadFileAsync(sourceFilePath, destinationFilePath);
        }

        public async Task<IEnumerable<StorageFile>> GetFilesAsync(string directoryPath)
        {
            directoryPath = directoryPath.ConvertBackSlashesToForwardSlashes();
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");
            return await containerClient.GetFilesAsync(directoryPath);
        }

        public async Task RemoveDirectoryAsync(string directoryPath)
        {
            directoryPath = directoryPath.ConvertBackSlashesToForwardSlashes();
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");
            await containerClient.RemoveDirectoryAsync(directoryPath);
        }

        public async Task<Uri> UploadFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken)
        {
            destinationFilePath = destinationFilePath.ConvertBackSlashesToForwardSlashes();
            var containerClient = GetBlobContainerClient() ?? throw new NullReferenceException("Failed to get instance of Azure Storage client");
            return await containerClient.UploadFileAsync(sourceFilePath, destinationFilePath, cancellationToken);
        }
    }
}
