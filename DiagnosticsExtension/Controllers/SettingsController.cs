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
            catch
            {
                return false;
            }

            return true;
        }

        private static string GenerateContainerSasUri(string accountName, string accountKey, string containerName)
        {
            Microsoft.WindowsAzure.Storage.Auth.StorageCredentials credentials = new Microsoft.WindowsAzure.Storage.Auth.StorageCredentials(accountName, accountKey);
            CloudStorageAccount storageAccount = new CloudStorageAccount(credentials, true);
            CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            container.CreateIfNotExists();

            //Set the expiry time and permissions for the container.
            //In this case no start time is specified, so the shared access signature becomes valid immediately.
            SharedAccessBlobPolicy sasConstraints = new SharedAccessBlobPolicy();
            sasConstraints.SharedAccessExpiryTime = new DateTime(9999, 12, 31, 23, 59, 59, 999, new GregorianCalendar(GregorianCalendarTypes.USEnglish), DateTimeKind.Utc);
            sasConstraints.Permissions = SharedAccessBlobPermissions.Write | SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Delete;

            //Generate the shared access signature on the container, setting the constraints directly on the signature.
            string sasContainerToken = container.GetSharedAccessSignature(sasConstraints);

            //Return the URI string for the container, including the SAS token.
            return container.Uri + sasContainerToken;
        }

        public bool Post([FromBody] Settings input)
        {
            //Simulate Delay
            //System.Threading.Thread.Sleep(20000);

            try
            {
                SessionController sessionController = new SessionController();
                if (input.BlobSasUri != "")
                {
                    if (input.BlobSasUri == "clear")
                    {
                        sessionController.BlobStorageSasUri = "";
                    }
                    else
                    {
                        if (!ValidateContainerSasUri(input.BlobSasUri))
                            return false;
                        sessionController.BlobStorageSasUri = input.BlobSasUri;
                    }

                    return true;
                }
                else
                {
                    var sasUri = GenerateContainerSasUri(input.BlobAccount, input.BlobKey, input.BlobContainer);
                    if (sasUri != null)
                    {
                        sessionController.BlobStorageSasUri = sasUri;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
