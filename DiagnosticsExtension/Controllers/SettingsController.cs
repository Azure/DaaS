//-----------------------------------------------------------------------
// <copyright file="SettingsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Auth;

namespace DiagnosticsExtension.Controllers
{
    public class SettingsController : ApiController
    {
        public Settings Get()
        {
            SessionController sessionController = new SessionController();

            List<string> diagnosers = new List<string>();
            diagnosers.Add("Event Viewer Logs");
            diagnosers.Add("Memory Dump");
            diagnosers.Add("Http Logs");
            diagnosers.Add("PHP error Logs");
            diagnosers.Add("PHP Process Report");

            Settings settings = new Settings
            {
                Diagnosers = new List<string>(diagnosers),
                TimeSpan = TimeSpan.FromMinutes(2).ToString(),
                BlobSasUri = sessionController.BlobStorageSasUri,
                BlobContainer = "",
                BlobKey = "",
                BlobAccount = ""
            };

            return settings;
        }

        private static bool ValidateContainerSasUri(string uri)
        {
            //Try performing container operations with the SAS provided.

            //Return a reference to the container using the SAS URI.
            CloudBlobContainer container = new CloudBlobContainer(new Uri(uri));

            try
            {
                container.ListBlobs();
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while validating SAS URI", ex);
                return false;
            }

            return true;
        }

        private static string GenerateContainerSasUri(string accountName, string accountKey, string containerName, string endpointSuffix)
        {
            StorageCredentials credentials = new StorageCredentials(accountName, accountKey);
            CloudStorageAccount storageAccount;
            if (string.IsNullOrWhiteSpace(endpointSuffix))
            {
                storageAccount = new CloudStorageAccount(credentials, useHttps: true);
            }
            else
            {
                storageAccount = new CloudStorageAccount(credentials, endpointSuffix, useHttps:true);
            }

            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExists();

            //Set the expiry time and permissions for the container.
            //In this case no start time is specified, so the shared access signature becomes valid immediately.
            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy
            {
                SharedAccessExpiryTime = new DateTime(9999, 12, 31, 23, 59, 59, 999, new GregorianCalendar(GregorianCalendarTypes.USEnglish), DateTimeKind.Utc),
                Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Delete
            };

            //Generate the shared access signature on the container, setting the constraints directly on the signature.
            string sasContainerToken = container.GetSharedAccessSignature(sasConstraints);

            //Return the URI string for the container, including the SAS token.
            return container.Uri + sasContainerToken;
        }

        public HttpResponseMessage Post([FromBody] Settings settings)
        {
            try
            {
                SessionController sessionController = new SessionController();
                if (settings.BlobSasUri != "")
                {
                    if (settings.BlobSasUri == "clear")
                    {
                        sessionController.BlobStorageSasUri = "";
                    }
                    else
                    {
                        if (!ValidateContainerSasUri(settings.BlobSasUri))
                            return Request.CreateResponse(HttpStatusCode.OK, false);
                        sessionController.BlobStorageSasUri = settings.BlobSasUri;
                    }

                    return Request.CreateResponse(HttpStatusCode.OK, true);
                }
                else
                {
                    var sasUri = GenerateContainerSasUri(settings.BlobAccount, settings.BlobKey, settings.BlobContainer, settings.EndpointSuffix);
                    if (!string.IsNullOrWhiteSpace(sasUri))
                    {
                        sessionController.BlobStorageSasUri = sasUri;
                        return Request.CreateResponse(HttpStatusCode.OK, true);
                    }
                    else
                    {
                        return Request.CreateResponse(HttpStatusCode.OK, false);
                    }
                }
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while changing settings", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }
    }
}
