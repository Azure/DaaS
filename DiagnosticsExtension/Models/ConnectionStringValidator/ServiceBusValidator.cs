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

        ConnectionStringValidationResult.ManagedIdentityType identityType;
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
                response = ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, identityType);
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
                response = ConnectionStringResponseUtility.EvaluateResponseStatus(e, Type, identityType);
            }

            return response;
        }
        protected async Task<TestConnectionData> TestConnectionStringViaAppSettingAsync(string appSettingName, string entityName)
        {
            string appSettingClientIdValue, appSettingClientCredValue = "";
            ServiceBusClient client = null;
            var envDict = Environment.GetEnvironmentVariables();

            if (envDict.Contains(appSettingName))
            {
                try
                {
                    string connectionString = Environment.GetEnvironmentVariable(appSettingName);
                    client = new ServiceBusClient(connectionString);
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
                            identityType = ConnectionStringValidationResult.ManagedIdentityType.User;
                            client = new ServiceBusClient(serviceUriString, new Azure.Identity.ManagedIdentityCredential(appSettingClientIdValue));
                        }
                    }
                    // Creating client using System assigned managed identity
                    else
                    {
                        identityType = ConnectionStringValidationResult.ManagedIdentityType.System;
                        client = new ServiceBusClient(serviceUriString, new Azure.Identity.ManagedIdentityCredential());
                    }
                }
                catch (Exception e)
                {
                    throw new ManagedIdentityException(e.Message, e);
                }
            }
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = client.ToString(),
                Succeeded = true
            };
            ServiceBusReceiverOptions opt = new ServiceBusReceiverOptions();
            opt.ReceiveMode = ServiceBusReceiveMode.PeekLock;
            opt.PrefetchCount = 1;
            ServiceBusReceiver receiver = client.CreateReceiver(entityName, opt);
            ServiceBusReceivedMessage receivedMessage = await receiver.PeekMessageAsync();

            return data;
        }
    }
}
