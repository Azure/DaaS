using DiagnosticsExtension.Models;
using System;
using System.Net.Http;
using System.Web.Http;
using DaaS.Storage;
using Microsoft.Azure.Storage;

namespace DiagnosticsExtension.Controllers
{
    public class SettingsController : ApiController
    {
        private readonly IStorageService _storageService;

        public SettingsController(IStorageService storageService) 
        {
            _storageService = storageService;
        }

        [HttpPost]
        [Route("api/settings")]
        public Settings Post()
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
                sasUriResponse.IsValidStorageConnectionString = sessionController.IsValidStorageConnectionString(out string storageConnectionStringEx);
                sasUriResponse.StorageConnectionStringException = storageConnectionStringEx;

                if (_storageService.ValidateStorageConfiguration(out string storageAccount, out Exception exceptionCallingStorage))
                {
                    sasUriResponse.StorageAccount = storageAccount;
                    sasUriResponse.IsValid = true;
                }
                else
                {
                    sasUriResponse.StorageAccount = storageAccount;
                    sasUriResponse.IsValid = false;
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
    }
}
