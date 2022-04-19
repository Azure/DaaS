// -----------------------------------------------------------------------
// <copyright file="ServiceBusValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Azure.ServiceBus.Management;
using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class ServiceBusValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.Azure.ServiceBus";
        public ConnectionStringType Type => ConnectionStringType.ServiceBus;

        public Task<bool> IsValidAsync(string connectionString)
        {
            try
            {
                new ServiceBusConnectionStringBuilder(connectionString);
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

        protected async Task<TestConnectionData> TestConnectionStringAsync(string connectionString, string name = null, string clientId = null)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                throw new EmptyConnectionStringException();
            }
            ServiceBusConnectionStringBuilder connectionStringBuilder = null;
            try
            {
                connectionStringBuilder = new ServiceBusConnectionStringBuilder(connectionString);
            }
            catch (Exception e)
            {
                throw new MalformedConnectionStringException(e.Message, e);
            }
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name,
                Succeeded = true
            };

            MessageReceiver msgReceiver = new MessageReceiver(connectionStringBuilder, ReceiveMode.PeekLock, prefetchCount: 1);
            Message msg = await msgReceiver.PeekAsync();

            return data;
        }
        async public Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appSettingName, string entityName)
        {
            ConnectionStringValidationResult response = new ConnectionStringValidationResult(Type);
            bool isManagedIdentityConnection = false;
            try
            {
                string appSettingClientIdValue, appSettingClientCredValue = "";
                ServiceBusClient client = null;
                var envDict = Environment.GetEnvironmentVariables();

                if (envDict.Contains(appSettingName))
                {
                    try
                    {
                        string connectionString = Environment.GetEnvironmentVariable(appSettingName);
                        if (string.IsNullOrEmpty(connectionString))
                        {
                            throw new EmptyConnectionStringException();
                        }
                        connectionString += ";EntityPath=" + entityName;
                        client = new ServiceBusClient(connectionString);
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
                            client = new ServiceBusClient(serviceUriString, ManagedIdentityCredentialTokenValidator.GetValidatedCredential(appSettingClientIdValue, appSettingName));
                        }
                        // Creating client using System assigned managed identity
                        else
                        {
                            response.IdentityType = Constants.System;
                            client = new ServiceBusClient(serviceUriString, new Azure.Identity.ManagedIdentityCredential());
                        }
                    }
                    else
                    {
                        string fullyQualifiedNamespaceAppSettingName = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith(appSettingName) && k.ToLower().EndsWith("fullyqualifiednamespace")).FirstOrDefault();
                        if (fullyQualifiedNamespaceAppSettingName == null)
                        {
                            throw new ManagedIdentityException(Constants.ServiceBusFQMissingSummary);
                        }
                        throw new ManagedIdentityException(String.Format(Constants.ServiceBusFQNSEmptySummary, fullyQualifiedNamespaceAppSettingName));

                    }
                }
                ServiceBusReceiverOptions opt = new ServiceBusReceiverOptions();
                opt.ReceiveMode = ServiceBusReceiveMode.PeekLock;
                opt.PrefetchCount = 1;
                ServiceBusReceiver receiver = client.CreateReceiver(entityName, opt);
                ServiceBusReceivedMessage receivedMessage = await receiver.PeekMessageAsync();

                response.Status = ConnectionStringValidationResult.ResultStatus.Success;
            }
            catch (Exception e)
            {
                // TODO: Find out what exception class is thrown for the message below and add that to the set of conditions
                if (e.Message.Contains("The messaging entity") && e.Message.Contains("could not be found"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EntityNotFound;
                    response.StatusSummary = String.Format(Constants.ServiceBusEntityNotFoundSummary, entityName);
                    response.StatusDetails = Constants.ServiceBusEntityNotFoundDetails;
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
