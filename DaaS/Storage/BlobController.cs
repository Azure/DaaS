//-----------------------------------------------------------------------
// <copyright file="BlobController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Blob;

namespace DaaS.Storage
{
    public static class BlobController
    {
        private static readonly ConcurrentDictionary<string, CloudBlobContainer> Containers = new ConcurrentDictionary<string, CloudBlobContainer>();

        internal static CloudBlobDirectory GetBlobDirectory(string directoryPath, string blobSasUri)
        {
            // Blob storage uses forward slashes rather than back slashes
            directoryPath = directoryPath.ConvertBackSlashesToForwardSlashes();
            CloudBlobDirectory dirBlob = null;
            if (!string.IsNullOrWhiteSpace(blobSasUri))
            {
                blobSasUri = GetActualBlobSasUri(blobSasUri);
                if (!Containers.ContainsKey(blobSasUri))
                {
                    try
                    {
                        var container = new CloudBlobContainer(new Uri(blobSasUri));
                        var blobList = container.ListBlobs();
                        Containers.TryAdd(blobSasUri, container);
                        dirBlob = container.GetDirectoryReference(directoryPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogErrorEvent("Failed while accessing blob storage", ex);
                    }
                }
                else
                {
                    dirBlob = Containers[blobSasUri].GetDirectoryReference(directoryPath);
                }
            }
            return dirBlob;
        }

        internal static CloudBlockBlob GetBlobForFile(string relativeFilePath, string blobSasUri)
        {
            try
            {
                var dir = GetBlobDirectory(Path.GetDirectoryName(relativeFilePath), blobSasUri);
                CloudBlockBlob blockBlob = dir.GetBlockBlobReference(Path.GetFileName(relativeFilePath));
                return blockBlob;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool ValidateBlobSasUri(string blobSasUri, out Exception exceptionContactingStorage)
        {
            exceptionContactingStorage = null;
            blobSasUri = GetActualBlobSasUri(blobSasUri);
            try
            {
                var container = new CloudBlobContainer(new Uri(blobSasUri));
                var blobList = container.ListBlobsSegmented("", true, BlobListingDetails.None, 1, null, null, null);
                blobList.Results.Count();
                return true;
            }
            catch (Exception ex)
            {
                exceptionContactingStorage = ex;
                Logger.LogErrorEvent("Encountered exception while validating SAS URI", ex);
                return false;
            }
        }

        internal static string GetBlobStorageHostName(string blobSasUri)
        {
            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                return string.Empty;
            }
            try
            {
                blobSasUri = GetActualBlobSasUri(blobSasUri);
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

        public static string GetPermanentStoragePathOnBlob(string relativeFilePath, string blobSasUri)
        {
            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                throw new Exception("Blob storage isn't configured. Can't access a file from blob storage");
            }
            
            blobSasUri = GetActualBlobSasUri(blobSasUri);
            var blobUriSections = blobSasUri.Split('?');
            if (blobSasUri.Length <= 2)
            {
                throw new Exception("Invalid blob storage SaS Uri configured");
            }

            var path = blobUriSections[0] + "/" + relativeFilePath.ConvertBackSlashesToForwardSlashes() + "?" +
                       string.Join("?", blobUriSections, 1, blobUriSections.Length - 1);

            return path;
        }

        internal static string GetActualBlobSasUri(string blobSasUri)
        {
            //
            // BlobSasUri could be set to %WEBSITE_DAAS_STORAGE_SASURI%
            //

            if (string.IsNullOrWhiteSpace(blobSasUri))
            {
                return string.Empty;
            }

            if (blobSasUri.StartsWith("%"))
            {
                blobSasUri = Environment.ExpandEnvironmentVariables(blobSasUri);
            }
            return blobSasUri;
        }
    }
}
