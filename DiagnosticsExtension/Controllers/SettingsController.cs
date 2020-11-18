//-----------------------------------------------------------------------
// <copyright file="SettingsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Auth;
using DaaS;
using DaaS.Storage;

namespace DiagnosticsExtension.Controllers
{
    public class SettingsController : ApiController
    {
        [HttpGet]
        [Route("api/settings")]
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

        [HttpGet]
        [Route("api/settings/validatesasuri")]
        public HttpResponseMessage ValidateSasUri()
        {
            var sasUriResponse = new SasUriResponse();
            SessionController sessionController = new SessionController();
            
            string blobSasUri = DaaS.Configuration.Settings.GetBlobSasUriFromEnvironment(DaaS.Configuration.Settings.WebSiteDaasStorageSasUri, out bool configredAsEnvironment);

            if (configredAsEnvironment)
            {
                sasUriResponse.SpecifiedAt = "EnvironmentVariable";
            }
            else if (!string.IsNullOrEmpty(sessionController.BlobStorageSasUri))
            {
                sasUriResponse.SpecifiedAt = "PrivateSettings.xml";
                blobSasUri = sessionController.BlobStorageSasUri;
            }
            else
            {
                return Request.CreateResponse(sasUriResponse);
            }

            if (BlobController.ValidateBlobSasUri(blobSasUri, out Exception exceptionCallingStorage))
            {
                try
                {
                    Uri u = new Uri(blobSasUri);
                    sasUriResponse.StorageAccount = u.Host;
                    sasUriResponse.IsValid = true;

                }
                catch (Exception ex)
                {
                    sasUriResponse.Exception = ex.Message;
                    sasUriResponse.IsValid = false;
                }
            }
            else
            {
                sasUriResponse.IsValid = false;
                if (Uri.TryCreate(blobSasUri, UriKind.Absolute, out Uri outUri) && (outUri.Scheme == Uri.UriSchemeHttp || outUri.Scheme == Uri.UriSchemeHttps))
                {
                    sasUriResponse.StorageAccount = outUri.Host;
                }
                if (exceptionCallingStorage != null)
                {
                    sasUriResponse.Exception = exceptionCallingStorage.Message;
                    if (exceptionCallingStorage is StorageException storageEx)
                    {
                        if (storageEx.RequestInformation != null)
                        {
                            sasUriResponse.ExtendedError = new ExtendedError()
                            {
                                HttpStatusCode = storageEx.RequestInformation.HttpStatusCode,
                                HttpStatusMessage = storageEx.RequestInformation.HttpStatusMessage
                            };

                            if (storageEx.RequestInformation.ExtendedErrorInformation != null)
                            {
                                sasUriResponse.ExtendedError.ErrorCode = storageEx.RequestInformation.ExtendedErrorInformation.ErrorCode;
                                sasUriResponse.ExtendedError.ErrorMessage = storageEx.RequestInformation.ExtendedErrorInformation.ErrorMessage;
                                sasUriResponse.ExtendedError.AdditionalDetails = storageEx.RequestInformation.ExtendedErrorInformation.AdditionalDetails;
                            }
                        }
                    }
                }
            }

            return Request.CreateResponse(sasUriResponse);
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
                storageAccount = new CloudStorageAccount(credentials, endpointSuffix, useHttps: true);
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

        [HttpPost]
        [Route("api/settings")]
        public HttpResponseMessage Post([FromBody] Settings settings)
        {
            try
            {
                //
                // If the user is trying to change the Storage Account, make
                // sure that there is no active CPU monitoring session in progress
                //
                if (!string.IsNullOrWhiteSpace(settings.BlobAccount)
                    && !string.IsNullOrWhiteSpace(settings.BlobContainer)
                    && !string.IsNullOrWhiteSpace(settings.BlobKey)
                    && !string.IsNullOrWhiteSpace(DaaS.Configuration.Settings.Instance.BlobStorageSas)
                    && IsCpuMonitoringSessionActive())
                {
                    return Request.CreateErrorResponse(HttpStatusCode.Conflict, "It is not possible to change the storage account because there is an active CPU Monitoring session using this storage account");
                }

                SessionController sessionController = new SessionController();
                if (!string.IsNullOrWhiteSpace(settings.BlobSasUri))
                {
                    if (settings.BlobSasUri == "clear")
                    {
                        sessionController.BlobStorageSasUri = "";
                    }
                    else
                    {
                        if (!DaaS.Storage.BlobController.ValidateBlobSasUri(settings.BlobSasUri, out Exception exceptionCallingStorage))
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
            catch (StorageException ex)
            {
                var addtionalErrorDetails = "";
                if (ex.RequestInformation != null && ex.RequestInformation.ExtendedErrorInformation != null)
                {
                    var extendedError = ex.RequestInformation.ExtendedErrorInformation;
                    addtionalErrorDetails = $"{extendedError.ErrorCode}:{extendedError.ErrorMessage}";
                    if (extendedError.AdditionalDetails != null)
                    {
                        addtionalErrorDetails += $" {string.Join(",", extendedError.AdditionalDetails)}";
                    }
                }

                Logger.LogErrorEvent($"Encountered exception while changing settings - {addtionalErrorDetails}", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message + " " + addtionalErrorDetails);
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Encountered exception while changing settings", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private bool IsCpuMonitoringSessionActive()
        {
            var monitoringController = new MonitoringSessionController();
            return monitoringController.GetActiveSession() != null;
        }
    }
}
