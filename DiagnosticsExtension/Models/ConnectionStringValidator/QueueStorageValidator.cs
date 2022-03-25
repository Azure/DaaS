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
using Azure.Storage.Files;
using Azure.Storage.Queues.Models;
using Microsoft.WindowsAzure.Storage;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class QueueStorageValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.WindowsAzure.Storage";
        public ConnectionStringType Type => ConnectionStringType.QueueStorageAccount;
        public async Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appSettingName, string entityName)
        {
            ConnectionStringValidationResult response = new ConnectionStringValidationResult(Type);
            bool isManagedIdentityConnection = false;
            try
            {
                var envDict = Environment.GetEnvironmentVariables();
                string appSettingClientIdValue, appSettingClientCredValue = null;
                QueueServiceClient client = null;

                if (envDict.Contains(appSettingName))
                {
                    try
                    {
                        string connectionString = Environment.GetEnvironmentVariable(appSettingName);
                        client = new QueueServiceClient(connectionString);
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
                    isManagedIdentityConnection = true;
                    string serviceUriString = ManagedIdentityConnectionResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.queueServiceUri);
                    if (!string.IsNullOrEmpty(serviceUriString))
                    {
                        appSettingClientIdValue = ManagedIdentityConnectionResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.clientId);
                        appSettingClientCredValue = ManagedIdentityConnectionResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.credential);
                        Uri serviceUri = new Uri(serviceUriString);
                        // Creating client using User assigned managed identity
                        if (appSettingClientCredValue != null)
                        {
                            if (appSettingClientCredValue != Constants.ValidCredentialValue)
                            {
                                throw new ManagedIdentityException(String.Format(Constants.ManagedIdentityCredentialInvalid, appSettingName));
                            }
                            if (string.IsNullOrEmpty(appSettingClientIdValue))
                            {
                                throw new ManagedIdentityException(String.Format(Constants.ManagedIdentityClientIdNullorEmpty, appSettingName));
                            }
                            response.IdentityType = Constants.User;
                            client = new QueueServiceClient(serviceUri, ManagedIdentityCredentialTokenValidator.GetValidatedCredential(appSettingClientIdValue, appSettingName));
                        }
                        // Creating client using System assigned managed identity
                        else
                        {
                            response.IdentityType = Constants.System;
                            client = new QueueServiceClient(serviceUri, new Azure.Identity.ManagedIdentityCredential());
                        }
                    }
                    else
                    {
                        throw new ManagedIdentityException(String.Format(Constants.QueueServiceUriMissing, appSettingName));
                    }
                }
                var resultSegment =
                client.GetQueues(QueueTraits.Metadata, null, default)
                .AsPages(default, 10);
                foreach (Azure.Page<QueueItem> containerPage in resultSegment)
                {
                    foreach (QueueItem containerItem in containerPage.Values)
                    {
                        string containerName = containerItem.Name.ToString();
                    }
                }
                response.Status = ConnectionStringValidationResult.ResultStatus.Success;
            }
            catch (Exception e)
            {
                if (isManagedIdentityConnection)
                {
                    ManagedIdentityConnectionResponseUtility.EvaluateResponseStatus(e, Type, ref response, appSettingName);
                }
                else
                {
                    ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, ref response, appSettingName);
                }
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
