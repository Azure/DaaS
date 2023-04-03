// -----------------------------------------------------------------------
// <copyright file="DaasAdministerController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class DaasAdministerController : ApiController
    {
        [HttpPost]
        public string StartSessionController(bool shouldStop = false)
        {
            try
            {
                var sessionController = new DaaS.Sessions.SessionController();
                sessionController.StartSessionRunner();
            }
            catch (Exception ex)
            {
                HttpResponseMessage resp = new HttpResponseMessage();
                resp.StatusCode = HttpStatusCode.InternalServerError;
                resp.Content = new StringContent(ex.Message);
                throw new HttpResponseException(resp);
            }
            if (shouldStop)
            {
                //TODO
            }
            return "Session Started";
        }
    }
}
