using DiagnosticsExtension.Models;
using Microsoft.WindowsAzure.Storage;
using System;
using System.Net.Http;
using System.Web.Http;
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
            Settings settings = new Settings
            {
                BlobSasUri = sessionController.BlobStorageSasUri,
                StorageConnectionString = sessionController.StorageConnectionString
            };

            return settings;
        }

        [HttpGet]
        [Route("api/settings/validatesasuri")]
        public HttpResponseMessage ValidateSasUri()
        {
            var sasUriResponse = new SasUriResponse();

            try
            {
                var sessionController = new DaaS.Sessions.SessionController();
                string connectionString = sessionController.StorageConnectionString;
                sasUriResponse.StorageConnectionStringSpecified = !string.IsNullOrWhiteSpace(connectionString);
                sasUriResponse.IsValidStorageConnectionString = IsValidStorageConnectionString(connectionString, out string storageConnectionStringEx);
                sasUriResponse.StorageConnectionStringException = storageConnectionStringEx;

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
                        if (exceptionCallingStorage is StorageException storageEx
                            && storageEx.RequestInformation != null)
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
            catch (Exception ex)
            {
                sasUriResponse.Exception = ex.Message;
                sasUriResponse.IsValid = false;
            }

            return Request.CreateResponse(sasUriResponse);
        }

        private bool IsValidStorageConnectionString(string connectionString, out string storageConnectionStringEx)
        {
            storageConnectionStringEx = "";
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return false;
            }

            try
            {
                var account = CloudStorageAccount.Parse(connectionString);
                account.CreateCloudBlobClient();
                return true;
            }
            catch (Exception ex)
            {
                storageConnectionStringEx = ex.ToLogString();
            }

            return false;
        }
    }
}
