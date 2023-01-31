// -----------------------------------------------------------------------
// <copyright file="ConnectionStringValidationController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using DiagnosticsExtension.Models.ConnectionStringValidator;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/connectionstringvalidation")]
    public class ConnectionStringValidationController : ApiController
    {
        private readonly Dictionary<ConnectionStringType, IConnectionStringValidator> typeValidatorMap;

        public ConnectionStringValidationController()
        {
            // register all validators here, the order of validators decides the order of connection string matching
            var validators = new IConnectionStringValidator[]
            {
                new SqlServerValidator(),
                new MySqlValidator(),
                new KeyVaultValidator(),
                new StorageValidator(),
                new ServiceBusValidator(),
                new EventHubsValidator(),
                new HttpValidator()
            };
            typeValidatorMap = validators.ToDictionary(v => v.Type, v => v);
        }

        [HttpPost]
        [Route("validate")]
        public async Task<HttpResponseMessage> Validate([FromBody] ConnectionStringRequestBody requestBody)
        {
            if (string.IsNullOrWhiteSpace(requestBody.ConnectionString))
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "ConnectionString is not specified in the request body");
            }
            if (string.IsNullOrWhiteSpace(requestBody.Type))
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "Type is not specified in the request body");
            }

            var result = await Validate(requestBody.ConnectionString, requestBody.Type);
            return result;
        }

        [HttpPost]
        [Route("validateappsetting")]
        public async Task<HttpResponseMessage> ValidateAppSetting([FromBody] AppSettingRequestBody requestBody)
        {
            var envDict = Environment.GetEnvironmentVariables();
            if (envDict.Contains(requestBody.AppSettingName))
            {
                var connectionString = (string)envDict[requestBody.AppSettingName];
                return await Validate(connectionString, requestBody.Type);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"AppSetting {requestBody.AppSettingName} not found");
            }
        }

        private async Task<HttpResponseMessage> Validate(string connStr, string type)
        {
            bool success = Enum.TryParse(type, out ConnectionStringType csType);
            if (success && typeValidatorMap.ContainsKey(csType))
            {
                var result = await typeValidatorMap[csType].ValidateAsync(connStr);
                return Request.CreateResponse(HttpStatusCode.OK, result);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"Type '{type}' is not supported");
            }
        }
    }
}