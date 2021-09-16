// -----------------------------------------------------------------------
// <copyright file="MsiValidatorController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/msivalidator")]
    public class MsiValidatorController : ApiController
    {
        public async Task<HttpResponseMessage> Get(ResourceType? resourceType, string resource = null, string endpoint = null, string clientId = null)
        {
            if (!resourceType.HasValue)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"MSI Validator is available only for the Resource Type : keyvault, storage. To Provide Custom inputs use the query paramters '?resourceType=Custom&resource=<url of resource>' ");
            }
            else
            {
                switch (resourceType)
                {
                    case ResourceType.Sql:
                        return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"MSI Validator is available only for the Resource Type : keyvault, storage. For Sql, use /api/databasetest.");

                    case ResourceType.Custom:
                        if (resource == null)
                        {
                            return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"To use MSI Validator for Customer inputs, please specify resourceUrl");
                        }
                        break;
                }
            }

            try
            {
                MsiValidator msi = new MsiValidator();
                MsiValidatorInput input = new MsiValidatorInput(resourceType.Value, resource, endpoint, clientId);

                if (msi.IsEnabled())
                {
                    // Step 1 : Check if we are able to get an access token
                    bool connectivityWithAzureActiveDirectory = await msi.GetTokenAsync(input);

                    // Step 2 : Test Connectivity to endpoint
                    if (connectivityWithAzureActiveDirectory)
                        await msi.TestConnectivityAsync(input);
                }

                return Request.CreateResponse(HttpStatusCode.OK, msi.Result);
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Unable to validate Managed Identity configuration. Encountered the exception :", ex);
                return Request.CreateErrorResponse(HttpStatusCode.OK, ex.Message + ex.StackTrace);
            }
        }
    }
}