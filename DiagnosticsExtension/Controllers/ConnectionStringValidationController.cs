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
        private IConnectionStringValidator[] validators;
        private Dictionary<ConnectionStringType, IConnectionStringValidator> typeValidatorMap;

        public ConnectionStringValidationController()
        {
            // register all validators here, the order of validators decides the order of connection string matching
            validators = new IConnectionStringValidator[]
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

        [HttpGet]
        [Route("validate")]
        public async Task<HttpResponseMessage> Validate(string connStr, string type)
        {
            bool success = Enum.TryParse(type, out ConnectionStringType csType);
            if (success)
            {
                return await Validate(connStr, (int)csType);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"type '{type}' is not supported");
            }
        }

        [HttpGet]
        [Route("validate")]
        public async Task<HttpResponseMessage> Validate(string connStr, int typeId)
        {
            var enumType = (ConnectionStringType)typeId;
            if (typeValidatorMap.ContainsKey(enumType))
            {
                var result = await typeValidatorMap[enumType].Validate(connStr);
                return Request.CreateResponse(HttpStatusCode.OK, result);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"No supported validator found for typeId={typeId}");
            }
        }

        [HttpPost]
        [Route("validate")]
        public async Task<HttpResponseMessage> Validate()
        {
            var body = await Request.Content.ReadAsStringAsync();
            string connStr = null;
            int? typeId = null;
            string type = null;
            try
            {
                var json = JsonConvert.DeserializeObject<JToken>(body);
                connStr = (string)json["connStr"];
                typeId = (int?)json["typeId"];
                type = (string)json["type"];
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    throw new Exception("Null or empty connection string");
                }
                if (typeId == null && type == null)
                {
                    throw new Exception("Either typeId or type are required");
                }
            }
            catch (Exception e)
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, e);
            }
            if (typeId == null && type != null) 
            {
                bool success = Enum.TryParse(type, out ConnectionStringType csType);
                if (success)
                {
                    typeId = (int) csType;
                }
                else
                {
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"type '{type}' is not supported");
                }
            }

            var result = await Validate(connStr, typeId.Value);
            return result;
        }

        [HttpGet]
        [Route("validateappsetting")]
        public async Task<HttpResponseMessage> ValidateAppSetting(string appSettingName, int typeId)
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
        [Route("validateappsetting")]
        public async Task<HttpResponseMessage> ValidateAppSetting(string appSettingName, string type)
        {
            bool success = Enum.TryParse(type, out ConnectionStringType csType);
            if (success)
            {
                return await ValidateAppSetting(appSettingName, (int)csType);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, $"type {type} is not supported");
            }
        }
    }
}