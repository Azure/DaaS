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

        public static string ValidateBlobSasUri(string blobSasUri)
        {
            var message = string.Empty;
            
            try
            {
                var container = new CloudBlobContainer(new Uri(blobSasUri));
                var blobList = container.ListBlobsSegmented("", true, BlobListingDetails.None, 1, null, null, null);
                blobList.Results.Count();
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            return message;
        }
    }
}
