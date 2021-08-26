// -----------------------------------------------------------------------
// <copyright file="DiagnosersV2Controller.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaaS.V2;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("diagnosers")]
    public class DiagnosersV2Controller : ApiController
    {
        private const string RelayPartySuiteErrorMessage = "Azure App Service sandbox component is disabled. This can happen if the App is running on ILB ASE with Relay Party suite service enabled. For this configuration, none of the diagnostic tools work.";
        private readonly ISessionManager _sessionManager;

        public DiagnosersV2Controller(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        public HttpResponseMessage Get()
        {
            try
            {
                if (!_sessionManager.IsSandboxAvailable())
                {
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, RelayPartySuiteErrorMessage);
                }

                var retVal = _sessionManager.GetDiagnosers();
                return Request.CreateResponse(HttpStatusCode.OK, retVal);
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while getting diagnosers", ex);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }
    }
}
