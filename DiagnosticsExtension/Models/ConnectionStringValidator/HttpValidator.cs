// -----------------------------------------------------------------------
// <copyright file="HttpValidator.cs" company="Microsoft Corporation">
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
    public class HttpValidator : IConnectionStringValidator
    {
        public string ProviderName => "System.Net.Http";

        public ConnectionStringType Type => ConnectionStringType.Http;

        public async Task<bool> IsValidAsync(string connStr)
        {
            try
            {
                var uri = new Uri(connStr);
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
                    if (e is HttpRequestException)
                    {
                        var inner = e.InnerException;
                        if (inner!= null && (inner.Message.StartsWith("The remote name could not be resolved") || inner.Message.StartsWith("Unable to connect to the remote server")))
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
                    response.Exception = e;
                }
            }

            return response;
        }
    }
}