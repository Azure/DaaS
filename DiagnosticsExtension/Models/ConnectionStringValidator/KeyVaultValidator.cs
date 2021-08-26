// -----------------------------------------------------------------------
// <copyright file="KeyVaultValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class KeyVaultValidator : IConnectionStringValidator
    {
        public string ProviderName => "System.Net.Http";

        public ConnectionStringType Type => ConnectionStringType.KeyVault;

        public async Task<bool> IsValidAsync(string connStr)
        {
            try
            {
                if (!connStr.StartsWith("http"))
                {
                    connStr += "https://";
                }
                var uri = new Uri(connStr);
                if (!uri.Host.ToLower().EndsWith("vault.azure.net"))
                {
                    throw new MalformedConnectionStringException("Malformed KeyVault connection string. A valid KV connection string should has hostname ends with vault.azure.net.");
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        async public Task<ConnectionStringValidationResult> ValidateAsync(string connStr, string clientId = null)
        {
            var response = new ConnectionStringValidationResult(Type);

            using (var client = new HttpClient())
            {
                try
                {
                    if (!connStr.StartsWith("http"))
                    {
                        connStr += "https://";
                    }
                    var uri = new Uri(connStr);
                    if (!uri.Host.ToLower().EndsWith("vault.azure.net"))
                    {
                        throw new MalformedConnectionStringException("Malformed KeyVault connection string. A valid KV connection string should has hostname ends with vault.azure.net.");
                    }

                    if (!uri.Query.Contains("api-version"))
                    {
                        if (uri.Query == string.Empty)
                        {
                            connStr += "?";
                        }
                        else
                        {
                            connStr += "&";
                        }
                        connStr += "api-version=7.1";
                    }

                    MsiValidator msi = new MsiValidator();
                    if (msi.IsEnabled())
                    {
                        MsiValidatorInput input = new MsiValidatorInput(ResourceType.KeyVault, clientId);
                        bool success = await msi.GetTokenAsync(input);
                        var msiResult = msi.Result.GetTokenTestResult;

                        if (success)
                        {
                            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", msiResult.TokenInformation.AccessToken);
                        }
                        else
                        {
                            response.Status = ConnectionStringValidationResult.ResultStatus.MsiFailure;
                            var e = new Exception(msiResult.ErrorDetails.Message);
                            e.Data["AdalError"] = msiResult.ErrorDetails;
                            throw e;
                        }
                    }

                    var resp = await client.GetAsync(connStr);
                    int statusCode = (int)resp.StatusCode;
                    response.Payload = $"StatusCode: {statusCode}";
                    if (resp.IsSuccessStatusCode)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.Success;
                    }
                    else if (statusCode == 401)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                    }
                    else if (statusCode == 403)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                    }
                    else if (statusCode == 404)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.ContentNotFound;
                    }
                    else
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.UnknownResponse;
                    }

                }
                catch (Exception e)
                {
                    response.Exception = e;
                    if (response.Status == null)
                    {
                        if (e is MalformedConnectionStringException)
                        {
                            response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                        }
                        else if (e is HttpRequestException)
                        {
                            var inner = e.InnerException;
                            if (inner != null && (inner.Message.StartsWith("The remote name could not be resolved") || inner.Message.StartsWith("Unable to connect to the remote server")))
                            {
                                response.Status = ConnectionStringValidationResult.ResultStatus.EndpointNotReachable;
                            }
                            else
                            {
                                response.Status = ConnectionStringValidationResult.ResultStatus.ConnectionFailure;
                            }
                        }
                        else
                        {
                            response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                        }
                    }
                }
            }

            return response;
        }
    }
}