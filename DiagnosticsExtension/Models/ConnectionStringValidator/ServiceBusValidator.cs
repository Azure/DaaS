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
        public Azure.Messaging.ServiceBus.ServiceBusReceiveMode ReceiveMode { get; set; }
        ConnectionStringValidationResult.ManagedIdentityType identityType;
        public async Task<bool> IsValidAsync(string connectionString)
        {
            try
            {
                new ServiceBusClient(connectionString);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
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
                if (e is MalformedConnectionStringException || e is ArgumentNullException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e is EmptyConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                }
                else if ((e is ArgumentException && e.Message.Contains("Authentication ")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                }
                else if (e is ArgumentException && e.Message.Contains("entityPath is null") ||
                         e.Message.Contains("HostNotFound") ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("The argument  is null or white space"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e.InnerException != null && e.InnerException.InnerException != null &&
                         e.InnerException.InnerException.Message.Contains("The remote name could not be resolved"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
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

        protected async Task<TestConnectionData> TestConnectionStringAsync(string connectionString, string name = null, string clientId = null)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                throw new EmptyConnectionStringException();
            }

            ServiceBusClient client = null;
            try
            {
                client = new ServiceBusClient(connectionString);

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

            ServiceBusSessionProcessorOptions opt = new ServiceBusSessionProcessorOptions();
            opt.ReceiveMode = ServiceBusReceiveMode.PeekLock;
            string entityPath = ServiceBusConnectionStringProperties.Parse(connectionString).EntityPath;
            ServiceBusReceiver receiver = client.CreateReceiver(entityPath);
            ServiceBusReceivedMessage receivedMessage = await receiver.PeekMessageAsync();

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
                if (e is MalformedConnectionStringException || e is ArgumentNullException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e.Message.Contains("managedidentitymissed"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.managedidentitymissed;
                }
                else if (e.Message.Contains("Unauthorized"))
                {
                    if (identityType == ConnectionStringValidationResult.ManagedIdentityType.User)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.userAssignedmanagedidentity;
                    }
                    else
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.systemAssignedmanagedidentity;
                    }
                }
                else if (e.Message.Contains("ManagedIdentityCredential"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.managedIdentityCredential;
                }
                else if (e.Message.Contains("fullyQualifiedNamespacemissed"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.fullyQualifiedNamespacemissed;
                }
                else if (e is EmptyConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                }
                else if ((e is ArgumentException && e.Message.Contains("Authentication ")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature") ||
                         e.Message.Contains("Unauthorized"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                }
                else if (e is ArgumentException && e.Message.Contains("entityPath is null") ||
                         e.Message.Contains("HostNotFound") ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("The argument  is null or white space"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e.InnerException != null && e.InnerException.InnerException != null &&
                         e.InnerException.InnerException.Message.Contains("The remote name could not be resolved"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
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
            ServiceBusClient client = null;
            var envDict = Environment.GetEnvironmentVariables();

            if (envDict.Contains(appSettingName))
            {
                try
                {
                    value = Environment.GetEnvironmentVariable(appSettingName);
                    client = new ServiceBusClient(value);
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
                    if (!string.IsNullOrEmpty(value))
                    {
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
                                client = new ServiceBusClient(value, new Azure.Identity.ManagedIdentityCredential(appSettingClientIdValue));
                            }
                        }
                        else
                        {
                            identityType = ConnectionStringValidationResult.ManagedIdentityType.System;
                            client = new ServiceBusClient(value, new Azure.Identity.ManagedIdentityCredential());
                        }
                    }
                    else
                    {
                        throw new ManagedIdentityException("fullyQualifiedNamespacemissed");
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
