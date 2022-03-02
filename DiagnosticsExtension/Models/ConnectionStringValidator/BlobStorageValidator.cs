// -----------------------------------------------------------------------
// <copyright file="StorageValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Blobs.Models;
using Microsoft.WindowsAzure.Storage;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class BlobStorageValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.WindowsAzure.Storage";
        public ConnectionStringType Type => ConnectionStringType.BlobStorageAccount;
        public ConnectionStringValidationResult response = null;
        public async Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appsettingname, string entityName)
        {
            response = new ConnectionStringValidationResult(Type);

            try
            {
                var result = await TestConnectionStringViaAppSettingAsync(appsettingname, entityName);
                if (result.Succeeded)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Success;
                }
                else
                {
                    throw new Exception("Unexpected state reached: result.Succeeded == false is unexpected!");
                }
            }
            catch (Exception e)
            {
                ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, ref response);
            }

            return response;
        }

        public async Task<TestConnectionData> TestConnectionStringViaAppSettingAsync(string appSettingName, string entityName)
        {
            var envDict = Environment.GetEnvironmentVariables();
            string appSettingClientIdValue, appSettingClientCredValue = null;
            BlobServiceClient client = null;

            if (envDict.Contains(appSettingName))
            {
                try
                {
                    string connectionString = Environment.GetEnvironmentVariable(appSettingName);
                    client = new BlobServiceClient(connectionString);
                }
                catch (ArgumentNullException e)
                {
                    throw new EmptyConnectionStringException(e.Message, e);
                }
                catch (Exception e)
                {
                    throw new MalformedConnectionStringException(e.Message, e);
                }
            }
            else
            {
                string serviceUriString = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.BlobServiceUri);
                if (string.IsNullOrEmpty(serviceUriString))
                {
                    serviceUriString = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.ServiceUri);
                }
                if (!string.IsNullOrEmpty(serviceUriString))
                {
                    appSettingClientIdValue = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.ClientId);
                    appSettingClientCredValue = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.Credential);
                    Uri serviceUri = new Uri(serviceUriString);
                    // Creating client using User assigned managed identity
                    if (!string.IsNullOrEmpty(appSettingClientIdValue))
                    {
                        if (appSettingClientCredValue != ConnectionStringResponseUtility.ValidCredentialValue)
                        {
                            throw new ManagedIdentityException(ConnectionStringResponseUtility.ManagedIdentityCredentialMissing);
                        }
                        else
                        {
                            response.IdentityType = ConnectionStringResponseUtility.User;
                            client = new BlobServiceClient(serviceUri, new Azure.Identity.ManagedIdentityCredential(appSettingClientIdValue));
                        }
                    }
                    // Creating client using System assigned managed identity
                    else
                    {
                        response.IdentityType = ConnectionStringResponseUtility.System;
                        client = new BlobServiceClient(serviceUri, new Azure.Identity.ManagedIdentityCredential());
                    }
                }
                else
                {
                    throw new ManagedIdentityException(ConnectionStringResponseUtility.ServiceUriMissing);
                }
            }
            client.GetBlobContainersAsync();
            var resultSegment =
                client.GetBlobContainers(BlobContainerTraits.Metadata, null, default)
                .AsPages(default, 10);
            //need to read at least one result item to confirm authorization check for connection
            resultSegment.Single();

            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = client.ToString(),
                Succeeded = true
            };
            return data;
        }

        public async Task<ConnectionStringValidationResult> ValidateAsync(string connStr, string clientId = null)
        {
            throw new NotImplementedException();
        }
        public async Task<bool> IsValidAsync(string connStr)
        {
            throw new NotImplementedException();
        }

    }
}
