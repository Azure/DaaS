//-----------------------------------------------------------------------
// <copyright file="UdpEchoTestController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using DiagnosticsExtension.Models.ConnectionStringValidator;

namespace DiagnosticsExtension.Controllers
{
    /// <summary>
    /// Worker instances are running an udp echo server on port 30000. This controller is for checking the connection between target 
    /// worker instance by pinging and checking the echoed result.
    /// </summary>
    [RoutePrefix("api/connectionstringvalidation")]
    public class ConnectionStringValidationController : ApiController
    {
        [HttpGet]
        [Route("test")]
        public async Task<HttpResponseMessage> Test(string connStr, int? typeId = null)
        {
            // register all validators here
            var typeValidatorMap = new IConnectionStringValidator[]
            {
                new SqlServerValidator()
            }.ToDictionary(v => v.Type, v => v);

            if (typeId != null)
            {
                var enumType = (ConnectionStringType)typeId.Value;
                if (typeValidatorMap.ContainsKey(enumType))
                {
                    var result = await typeValidatorMap[enumType].Validate(connStr);
                    return Request.CreateResponse(HttpStatusCode.OK, new { result, connStr });
                }
                else
                {
                    return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"No supported validator found for typeId={typeId.Value}");
                }
            }
            else
            {
                var exceptions = new List<Exception>();
                foreach (var p in typeValidatorMap)
                {
                    try
                    {
                        if (p.Value.IsValid(connStr))
                        {
                            var result = await p.Value.Validate(connStr);
                            return Request.CreateResponse(HttpStatusCode.OK, new { result, connStr });
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

        [HttpGet]
        [Route("testappsetting")]
        public async Task<HttpResponseMessage> TestAppSetting(string appSettingName, int? typeId = null)
        {
            var envDict = Environment.GetEnvironmentVariables();
            if (envDict.Contains(appSettingName))
            {
                var connectionString = (string)envDict[appSettingName];
                return await Test(connectionString, typeId);
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, $"AppSetting {appSettingName} not found");
            }
        }
    }
}