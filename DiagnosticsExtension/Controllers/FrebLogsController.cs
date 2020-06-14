//-----------------------------------------------------------------------
// <copyright file="FrebLogsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class FrebLogsController : ApiController
    {
        // GET: api/FrebLogs
        public async Task<HttpResponseMessage> Get()
        {
            try
            {
                var files = await FrebParser.GetFrebFiles();
                return Request.CreateResponse(HttpStatusCode.OK, files.OrderByDescending(x => x.DateCreated));
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
           
        }

    }
}
