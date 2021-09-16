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
    }
}
