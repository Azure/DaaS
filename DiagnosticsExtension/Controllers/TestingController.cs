//-----------------------------------------------------------------------
// <copyright file="TestingController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class TestingController : ApiController
    {
        // GET: api/Testing
        public async Task<HttpResponseMessage> Get()
        {
            bool causeSlowness = (DateTime.UtcNow.Minute % 2) == 0;
            if (causeSlowness)
            {
                await Task.Delay(5000);
                return Request.CreateResponse(HttpStatusCode.OK, "Done Sleeping");
            }
            else
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, "An error returned by controller");
            }
        }
    }
}
