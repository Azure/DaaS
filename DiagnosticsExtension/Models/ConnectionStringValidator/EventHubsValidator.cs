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
        public ConnectionStringValidationResult response = null;
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
            response = new ConnectionStringValidationResult(Type);

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
                ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, ref response);
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
            response = new ConnectionStringValidationResult(Type);

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
                ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, ref response);
            }

            return response;
        }
        protected async Task<TestConnectionData> TestConnectionStringViaAppSettingAsync(string appSettingName, string entityName)
        {
            string appSettingClientIdValue, appSettingClientCredValue = "";
            EventHubProducerClient client = null;
            var envDict = Environment.GetEnvironmentVariables();
            string eventHubName = entityName;

            if (envDict.Contains(appSettingName))
            {
                try
                {
                    string connectionString = Environment.GetEnvironmentVariable(appSettingName);
                    client = new EventHubProducerClient(connectionString);
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
                    string serviceUriString = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.FullyQualifiedNamespace);
                    appSettingClientIdValue = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.ClientId);
                    appSettingClientCredValue = Environment.GetEnvironmentVariable(appSettingName + ConnectionStringResponseUtility.Credential);
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
                            client = new EventHubProducerClient(serviceUriString, eventHubName, new ManagedIdentityCredential(appSettingClientIdValue));
                        }
                    }
                    // Creating client using System assigned managed identity
                    else
                    {
                        response.IdentityType = ConnectionStringResponseUtility.System;
                        client = new EventHubProducerClient(serviceUriString, eventHubName, new ManagedIdentityCredential());
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
