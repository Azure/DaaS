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

namespace DiagnosticsExtension.Controllers
{
    public class SettingsController : ApiController
    {
        public Settings Get()
        {
            SessionController sessionController = new SessionController();

            List<string> diagnosers = new List<string>();
            diagnosers.Add("foo");

            Settings settings = new Settings
            {
                Diagnosers = new List<string>(),
                TimeSpan = TimeSpan.FromMinutes(2).ToString(),
                BlobSasUri = sessionController.BlobStorageSasUri,
                BlobContainer = "",
                BlobKey = ""
            };

            return settings;
        }

        private String GenerateSasUri(string container, string key)
        {
            //http://azure.microsoft.com/en-us/documentation/articles/storage-dotnet-shared-access-signature-part-2/
            return "foo";
        }
        public bool Put([FromBody] Settings input)
        {
            SessionController sessionController = new SessionController();
            if (input.BlobSasUri != "")
            {
                sessionController.BlobStorageSasUri = input.BlobSasUri;
            }
            else
            {
                sessionController.BlobStorageSasUri = GenerateSasUri(input.BlobContainer, input.BlobKey);
            }

            return true;
        }
    }
}
