//-----------------------------------------------------------------------
// <copyright file="CrashController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DaaS.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DaaS
{
    public class CrashController
    {
        const string DirectoryPath = "CrashDumps";
        private readonly string _siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";

        public async Task<List<CrashMonitoringFile>> GetCrashDumpsAsync(bool includeFullUri = false)
        {
            bool checkForSiteName = ShouldCheckSiteNameInBlob();
            var filesCollected = new List<CrashMonitoringFile>();
            var blobSasUri = DaaS.Configuration.Settings.Instance.BlobStorageSas;
            if (!string.IsNullOrWhiteSpace(blobSasUri))
            {
                var dir = BlobController.GetBlobDirectory(DirectoryPath, blobSasUri);
                BlobContinuationToken blobContinuationToken = null;
                do
                {
                    var resultSegment = await dir.ListBlobsSegmentedAsync(
                        useFlatBlobListing: true,
                        blobListingDetails: BlobListingDetails.None,
                        maxResults: null,
                        currentToken: blobContinuationToken,
                        options: null,
                        operationContext: null
                    );

                    // Get the value of the continuation token returned by the listing call.
                    blobContinuationToken = resultSegment.ContinuationToken;
                    foreach (var item in resultSegment.Results.Cast<CloudBlockBlob>())
                    {
                        if (checkForSiteName && !item.Uri.Segments.Contains(_siteName + "/", StringComparer.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        var relativePath = item.Uri.ToString().Replace(item.Container.Uri.ToString() + "/", "");
                        string fileName = item.Uri.Segments.Last();
                        DateTime created = item.Properties.Created.HasValue ? item.Properties.Created.Value.UtcDateTime : DateTime.MinValue;
                        filesCollected.Add(new CrashMonitoringFile(fileName, relativePath, includeFullUri ? item.Uri : null, created));
                    }
                } while (blobContinuationToken != null); // Loop while the continuation token is not null.
            }
            return filesCollected;

        }

        private bool ShouldCheckSiteNameInBlob()
        {
            var shouldCheckSiteName = false;
            Assembly assembly;
            try
            {
                assembly = Assembly.Load("Microsoft.Web.Hosting, Version=7.1.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                if (fileVersionInfo.ProductMajorPart >= 90)
                {
                    shouldCheckSiteName = true;
                }
            }
            catch
            {
                shouldCheckSiteName = true;
            }
            return shouldCheckSiteName;
        }
    }
}
