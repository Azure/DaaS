//-----------------------------------------------------------------------
// <copyright file="UdpEchoTestController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using DiagnosticsExtension.Models;
using DiagnosticsExtension.Models.ConnectionStringValidator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/connectionstringvalidation")]
    public class ConnectionStringValidationController : ApiController
    {
        [HttpGet]
        [Route("validate")]
        public async Task<HttpResponseMessage> Validate(string connStr, int? typeId = null)
        {
            // register all validators here, the order of validators decides the order of connection string matching
            var validators = new IConnectionStringValidator[]
            {
                new SqlServerValidator(),
                new MySqlValidator(),
                new KeyVaultValidator(),
                new HttpValidator()
            };
            var typeValidatorMap = validators.ToDictionary(v => v.Type, v => v);

            if (typeId != null)
            {
                var enumType = (ConnectionStringType)typeId.Value;
                if (typeValidatorMap.ContainsKey(enumType))
                {
                    var result = await typeValidatorMap[enumType].Validate(connStr);
                    return Request.CreateResponse(HttpStatusCode.OK, result);
                }
                else
                {
                    return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"No supported validator found for typeId={typeId.Value}");
                }
            }
            else
            {
                var exceptions = new List<Exception>();
                foreach (var validator in validators)
                {
                    try
                    {
                        if (validator.IsValid(connStr))
                        {
                            var result = await validator.Validate(connStr);
                            return Request.CreateResponse(HttpStatusCode.OK, result);
                        }
                    }
                    catch (Exception e)
                    {
                        exceptions.Add(e);
                    }
                }
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, new AggregateException($"No supported validator found for provided connection string", exceptions));
            }
        }

        [HttpPost]
        [Route("validate")]
        public async Task<HttpResponseMessage> Validate()
        {
            var body = await Request.Content.ReadAsStringAsync();
            string connStr = null;
            int? typeId = null;
            try
            {
                var json = JsonConvert.DeserializeObject<JToken>(body);
                connStr = (string)json["connStr"];
                typeId = (int?)json["typeId"];
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    throw new Exception("Null or empty connection string");
                }
            }
            catch (Exception e)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, e);
            }
            var result = await Validate(connStr, typeId);
            return result;
        }

        [HttpGet]
        [Route("validateappsetting")]
        public async Task<HttpResponseMessage> ValidateAppSetting(string appSettingName, int? typeId = null)
        {
            var envDict = Environment.GetEnvironmentVariables();
            if (envDict.Contains(appSettingName))
            {
                var connectionString = (string)envDict[appSettingName];
                return await Validate(connectionString, typeId);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"AppSetting {appSettingName} not found");
            }
        }

        [HttpGet]
        [Route("test")]
        public async Task<IHttpActionResult> Test()
        {
            try
            {
                MsiValidator msi = new MsiValidator();
                MsiValidatorInput input = new MsiValidatorInput(ResourceType.KeyVault, "fdc4ce08-1981-464a-a490-1604c022df3f");
                bool success = await msi.GetTokenAsync(input);

                return Ok(new[] { msi.Result.GetTokenTestResult.TokenInformation.AccessToken });
            }
            catch (Exception e)
            {
                return Ok(e);
            }
        }
    }
}