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
        ConnectionStringValidationResult.ManagedIdentityType identityType;

        public async Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appsettingname, string entityName)
        {
            var response = new ConnectionStringValidationResult(Type);

            try
            {
                var result = await TestConnectionStringViaAppSetting(appsettingname, entityName);
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
                if (e is MalformedConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e.Message.Contains("managedidentitymissed"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityCredentialMissing;
                }
                else if (e.Message.Contains("Unauthorized") || e.Message.Contains("AuthorizationPermissionMismatch"))
                {
                    if (identityType == ConnectionStringValidationResult.ManagedIdentityType.User)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.UserAssignedManagedIdentity;
                    }
                    else
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.SystemAssignedManagedIdentity;
                    }
                }
                else if (e.Message.Contains("ManagedIdentityCredential"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityCredential;
                }
                else if (e.Message.Contains("fullyQualifiedNamespace"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.FullyQualifiedNamespaceMissed;
                }
                else if (e is EmptyConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                }
                else if (e.InnerException != null &&
                         e.InnerException.Message.Contains("The remote name could not be resolved"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                }
                else if (e is StorageException)
                {
                    if (((StorageException)e).RequestInformation.HttpStatusCode == 401)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                    }
                    else if (((StorageException)e).RequestInformation.HttpStatusCode == 403)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                    }
                }
                else
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                }
                response.Exception = e;
            }

            return response;
        }

        public async Task<TestConnectionData> TestConnectionStringViaAppSetting(string appSettingName, string entityName)
        {
            var envDict = Environment.GetEnvironmentVariables();
            string appSettingClientIdValue, appSettingClientCredValue = null;
            string value = null;
            QueueServiceClient client = null;

            if (envDict.Contains(appSettingName))
            {
                try
                {
                    value = Environment.GetEnvironmentVariable(appSettingName);
                    client = new QueueServiceClient(value);
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
                try
                {
                    value = Environment.GetEnvironmentVariable(appSettingName + "__queueServiceUri");                    
                    appSettingClientIdValue = Environment.GetEnvironmentVariable(appSettingName + "__clientId");
                    appSettingClientCredValue = Environment.GetEnvironmentVariable(appSettingName + "__credential");
                    Uri objuri = new Uri(value);
                    if (!string.IsNullOrEmpty(appSettingClientIdValue))
                    {
                        if (appSettingClientCredValue != "managedidentity")
                        {
                            throw new ManagedIdentityException("managedidentitymissed");
                        }
                        else
                        {
                            identityType = ConnectionStringValidationResult.ManagedIdentityType.User;
                            client = new QueueServiceClient(objuri, new Azure.Identity.ManagedIdentityCredential(appSettingClientIdValue));
                        }
                    }
                    else
                    {
                        identityType = ConnectionStringValidationResult.ManagedIdentityType.System;
                        client = new QueueServiceClient(objuri, new Azure.Identity.ManagedIdentityCredential());
                    }
                }
                catch (Exception e)
                {
                    throw new ManagedIdentityException(e.Message, e);
                }
            }
            client.GetQueuesAsync();
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
