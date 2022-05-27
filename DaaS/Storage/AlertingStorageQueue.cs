// -----------------------------------------------------------------------
// <copyright file="AlertingStorageQueue.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using DaaS.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;

namespace DaaS.Storage
{
    public class AlertingStorageQueue
    {
        private const int MessageExpirationInMinutes = 30;
        private CloudQueueClient _cloudQueueClient;

        public AlertingStorageQueue()
        {
            string sasUri = Settings.Instance.AccountSasUri;
            string connectionString = Settings.Instance.StorageConnectionString;
            InitializeQueueClient(sasUri, connectionString);
        }

        private void InitializeQueueClient(string sasUri, string connectionString)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    CloudStorageAccount cloudStorageAccount = CloudStorageAccount.Parse(connectionString);
                    _cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();
                }
                else if (!string.IsNullOrWhiteSpace(sasUri))
                {
                    Uri uriStorage = new Uri(sasUri);
                    if (!HasQueuePermissions(uriStorage))
                    {
                        Logger.LogVerboseEvent("SAS URI does not have enough permissions to write to Storage Queue");
                        return;
                    }

                    var accountName = GetAccountName(uriStorage);
                    var sasToken = GetSasToken(uriStorage);

                    StorageCredentials storageCredentials = new StorageCredentials(sasToken);
                    CloudStorageAccount cloudStorageAccount = new CloudStorageAccount(storageCredentials, accountName, endpointSuffix: null, useHttps: true);
                    _cloudQueueClient = cloudStorageAccount.CreateCloudQueueClient();
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Unhandled exception while initializing AlertingStorageQueue", ex);
            }
        }

        private bool HasQueuePermissions(Uri sasUri)
        {
            NameValueCollection queryParameters = GetQueryStringParameters(sasUri);

            var requiredPermissions = new List<char> { 'a', 'c', 'd', 'l', 'p', 'r', 'u', 'w' };

            return queryParameters["ss"].Contains("q") &&
                requiredPermissions.All(item => queryParameters["sp"].Contains(item));
        }

        private static NameValueCollection GetQueryStringParameters(Uri sasUri)
        {
            NameValueCollection queryParameters = new NameValueCollection();
            string[] querySegments = sasUri.Query.Split('&');
            foreach (string segment in querySegments)
            {
                string[] parts = segment.Split('=');
                if (parts.Length > 0)
                {
                    string key = parts[0].Trim(new char[] { '?', ' ' });
                    string val = parts[1].Trim();

                    queryParameters.Add(key, val);
                }
            }

            return queryParameters;
        }

        private string GetAccountName(Uri sasUri)
        {
            return sasUri.Host.Split('.')[0];
        }

        private string GetSasToken(Uri sasUri)
        {
            return sasUri.Query;
        }

        internal bool WriteMessageToAzureQueue(string message)
        {
            if (_cloudQueueClient == null)
            {
                return false;
            }

            var cloudQueue = _cloudQueueClient.GetQueueReference(Settings.AlertQueueName);
            cloudQueue.CreateIfNotExists();
            cloudQueue.AddMessage(new CloudQueueMessage(message), TimeSpan.FromMinutes(MessageExpirationInMinutes));
            return true;
        }
    }
}
