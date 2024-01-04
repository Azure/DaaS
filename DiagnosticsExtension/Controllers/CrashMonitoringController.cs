﻿// -----------------------------------------------------------------------
// <copyright file="CrashMonitoringController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using DaaS;
using DaaS.Storage;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/CrashMonitoring")]
    public class CrashMonitoringController : ApiController
    {
        private readonly IStorageService _storageService;

        public CrashMonitoringController(IStorageService storageService) 
        {
            _storageService = storageService;
        }

        [Route("memorydumps")]
        public async Task<HttpResponseMessage> Get()
        {
            try
            {
                var controller = new CrashController(_storageService);
                var files = await controller.GetCrashDumpsAsync();
                return Request.CreateResponse(HttpStatusCode.OK, files);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex);
            }
        }

        [HttpPost]
        [Route("memorydumps")]
        public async Task<HttpResponseMessage> Post()
        {
            try
            {
                var controller = new CrashController(_storageService);
                var files = await controller.GetCrashDumpsAsync(includeFullUri:true);
                return Request.CreateResponse(HttpStatusCode.OK, files);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex);
            }
        }

    }
}
