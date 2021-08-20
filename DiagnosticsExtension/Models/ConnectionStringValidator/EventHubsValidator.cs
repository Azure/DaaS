//-----------------------------------------------------------------------
// <copyright file="EventHubsValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
using Microsoft.Azure.EventHubs;
using System;
using System.Threading.Tasks;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class EventHubsValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.Azure.EventHubs";

        public ConnectionStringType Type => ConnectionStringType.EventHubs;

        public async Task<bool> IsValidAsync(string connectionString)
        {
            try
            {
                new EventHubsConnectionStringBuilder(connectionString);
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
    }
}