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
using System.Linq;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class EventHubsValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.Azure.EventHubs";
        public ConnectionStringType Type => ConnectionStringType.EventHubs;
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
            ConnectionStringValidationResult response = new ConnectionStringValidationResult(Type);

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

        async public Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appSettingName, string entityName)
        {
            ConnectionStringValidationResult response = new ConnectionStringValidationResult(Type);
            bool isManagedIdentityConnection = false;
            try
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
                        if (string.IsNullOrEmpty(connectionString))
                        {
                            throw new EmptyConnectionStringException();
                        }
                        connectionString += ";EntityPath=" + eventHubName;
                        client = new EventHubProducerClient(connectionString);
                    }
                    catch (EmptyConnectionStringException e)
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
                    string serviceUriString = ManagedIdentityConnectionResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.fullyQualifiedNamespace);
                    if (!string.IsNullOrEmpty(serviceUriString))
                    {
                        string clientIdAppSettingKey = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith(appSettingName) && k.ToLower().EndsWith("clientid")).FirstOrDefault();
                        appSettingClientIdValue = ManagedIdentityConnectionResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.clientId);
                        appSettingClientCredValue = ManagedIdentityConnectionResponseUtility.ResolveManagedIdentityCommonProperty(appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty.credential);
                        if (appSettingClientCredValue != null && appSettingClientCredValue != Constants.ValidCredentialValue)
                        {
                            throw new ManagedIdentityException(String.Format(Constants.ManagedIdentityCredentialInvalidSummary, appSettingName));
                        }
                        // If the user has configured __credential with "managedidentity" and set an app setting for __clientId (even if its empty) we assume their intent is to use a user assigned managed identity
                        if (appSettingClientCredValue != null && clientIdAppSettingKey != null)
                        {
                            if (string.IsNullOrEmpty(appSettingClientIdValue))
                            {
                                throw new ManagedIdentityException(String.Format(Constants.ManagedIdentityClientIdEmptySummary, clientIdAppSettingKey),
                                                                   String.Format(Constants.ManagedIdentityClientIdEmptyDetails, appSettingName));
                            }
                            response.IdentityType = Constants.User;
                            client = new EventHubProducerClient(serviceUriString, eventHubName, ManagedIdentityCredentialTokenValidator.GetValidatedCredential(appSettingClientIdValue, appSettingName));
                        }
                        // Creating client using System assigned managed identity
                        else
                        {
                            response.IdentityType = Constants.System;
                            client = new EventHubProducerClient(serviceUriString, eventHubName, new ManagedIdentityCredential());
                        }
                    }
                    else
                    {
                        string fullyQualifiedNamespaceAppSettingName = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith(appSettingName) && k.ToLower().EndsWith("fullyqualifiednamespace")).FirstOrDefault();
                        if (fullyQualifiedNamespaceAppSettingName == null)
                        {
                            throw new ManagedIdentityException(Constants.EventHubFQMissingSummary);
                        }
                        throw new ManagedIdentityException(String.Format(Constants.EventHubFQNSEmptySummary, fullyQualifiedNamespaceAppSettingName));

                    }
                }
                await client.GetPartitionIdsAsync();
                await client.CloseAsync();

                response.Status = ConnectionStringValidationResult.ResultStatus.Success;
            }
            catch (Exception e)
            {
                // TODO: Find out what exception class is thrown for the message below and add that to the set of conditions
                if (e.Message.Contains("The messaging entity") && e.Message.Contains("could not be found"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EntityNotFound;
                    response.StatusSummary = String.Format(Constants.EventHubEntityNotFoundSummary, entityName);
                    response.StatusDetails = Constants.EventHubEntityNotFoundDetails;
                    response.Exception = e;
                }
                else if (isManagedIdentityConnection)
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
    }
}
