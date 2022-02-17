// -----------------------------------------------------------------------
// <copyright file="EventHubsValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
using Microsoft.Azure.EventHubs;
using System;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Messaging.EventHubs.Producer;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class EventHubsValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.Azure.EventHubs";

        public ConnectionStringType Type => ConnectionStringType.EventHubs;
        ConnectionStringValidationResult.ManagedIdentityType identityType;

        public Task<bool> IsValidAsync(string connectionString)
        {
            try
            {
                new EventHubsConnectionStringBuilder(connectionString);
            }
            catch (Exception)
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }

        async public Task<ConnectionStringValidationResult> ValidateAsync(string connectionString, string clientId = null)
        {
            var response = new ConnectionStringValidationResult(Type);

            try
            {
                var result = await TestConnectionStringAsync(connectionString, null, clientId);
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
                else if (e is EmptyConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                }
                else if (e is ArgumentNullException ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("was not found"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e.Message.Contains("No such host is known"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                }
                else if (e.Message.Contains("InvalidSignature"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                }
                else if (e.Message.Contains("Ip has been prevented to connect to the endpoint"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                }
                else
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                }
                response.Exception = e;
            }

            return response;
        }

        protected async Task<TestConnectionData> TestConnectionStringAsync(string connectionString, string name, string clientId)
        {
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name,
                Succeeded = true
            };
            var client = EventHubClient.CreateFromConnectionString(connectionString);
            await client.GetRuntimeInformationAsync();
            await client.CloseAsync();

            return data;
        }

        async public Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appsettingName, string entityName)
        {
            var response = new ConnectionStringValidationResult(Type);

            try
            {
                var result = await TestConnectionStringViaAppSettingAsync(appsettingName, entityName);
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
                else if (e.Message.Contains("Unauthorized") || e.Message.Contains("unauthorized"))
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
                else if (e is ArgumentNullException ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("was not found"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e.Message.Contains("No such host is known"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                }
                else if (e.Message.Contains("InvalidSignature") ||
                         e.Message.Contains("Unauthorized"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                }
                else if (e.Message.Contains("Ip has been prevented to connect to the endpoint"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                }
                else
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                }
                response.Exception = e;
            }

            return response;
        }
        protected async Task<TestConnectionData> TestConnectionStringViaAppSettingAsync(string appSettingName, string entityName)
        {
            string value, appSettingClientIdValue, appSettingClientCredValue = "";
            EventHubProducerClient client = null;
            var envDict = Environment.GetEnvironmentVariables();
            string eventHubName = entityName;

            if (envDict.Contains(appSettingName))
            {
                try
                {
                    value = Environment.GetEnvironmentVariable(appSettingName);
                    client = new EventHubProducerClient(value);
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
                    value = Environment.GetEnvironmentVariable(appSettingName + "__fullyQualifiedNamespace");                    
                    appSettingClientIdValue = Environment.GetEnvironmentVariable(appSettingName + "__clientId");
                    appSettingClientCredValue = Environment.GetEnvironmentVariable(appSettingName + "__credential");
                    if (!string.IsNullOrEmpty(appSettingClientIdValue))
                    {
                        if (appSettingClientCredValue != "managedidentity")
                        {
                            throw new ManagedIdentityException("managedidentitymissed");
                        }
                        else
                        {
                            identityType = ConnectionStringValidationResult.ManagedIdentityType.User;
                            client = new EventHubProducerClient(value, eventHubName, new ManagedIdentityCredential(appSettingClientIdValue));
                        }
                    }
                    else
                    {
                        identityType = ConnectionStringValidationResult.ManagedIdentityType.System;
                        client = new EventHubProducerClient(value, eventHubName, new ManagedIdentityCredential());
                    }                    
                }
                catch (Exception e)
                {
                    throw new ManagedIdentityException(e.Message, e);
                }

            }
            await client.GetPartitionIdsAsync();
            await client.CloseAsync();

            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = client.ToString(),
                Succeeded = true
            };

            return data;
        }
    }
}
