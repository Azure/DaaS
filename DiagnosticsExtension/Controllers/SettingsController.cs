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
            var sessionController = new DaaS.Sessions.SessionController();

            List<string> diagnosers = new List<string>();
            diagnosers.Add("Event Viewer Logs");
            diagnosers.Add("Memory Dump");
            diagnosers.Add("Http Logs");
            diagnosers.Add("PHP error Logs");
            diagnosers.Add("PHP Process Report");

            Settings settings = new Settings
            {
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
            var sessionController = new DaaS.Sessions.SessionController();

            string blobSasUri = sessionController.BlobStorageSasUri;
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

    }
}
