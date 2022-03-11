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
        public async Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appSettingName, string entityName)
        {
            ConnectionStringValidationResult response = new ConnectionStringValidationResult(Type);

            try
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
                    string serviceUriString = ConnectionStringResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.blobServiceUri);
                    if (string.IsNullOrEmpty(serviceUriString))
                    {
                        serviceUriString = ConnectionStringResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.serviceUri);
                    }
                    if (!string.IsNullOrEmpty(serviceUriString))
                    {
                        appSettingClientIdValue = ConnectionStringResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.clientId);
                        appSettingClientCredValue = ConnectionStringResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.credential);
                        Uri serviceUri = new Uri(serviceUriString);
                        // Creating client using User assigned managed identity
                        if (appSettingClientCredValue != null)
                        {
                            if (appSettingClientCredValue != ConnectionStringResponseUtility.ValidCredentialValue)
                            {
                                throw new ManagedIdentityException(ConnectionStringResponseUtility.ManagedIdentityCredentialInvalid);
                            }
                            if (string.IsNullOrEmpty(appSettingClientIdValue))
                            {
                                throw new ManagedIdentityException(ConnectionStringResponseUtility.ManagedIdentityClientIdEmpty);
                            }
                            response.IdentityType = ConnectionStringResponseUtility.User;
                            client = new BlobServiceClient(serviceUri, new Azure.Identity.ManagedIdentityCredential(appSettingClientIdValue));
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

                response.Status = ConnectionStringValidationResult.ResultStatus.Success;
            }
            catch (Exception e)
            {
                ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, ref response);
            }

            return response;
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
