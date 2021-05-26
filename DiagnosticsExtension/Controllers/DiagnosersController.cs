//-----------------------------------------------------------------------
// <copyright file="DiagnosersController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DiagnosticsExtension.Models;
using DaaS.Diagnostics;
using DaaS.Sessions;

namespace DiagnosticsExtension.Controllers
{
    public class DiagnosersController : ApiController
    {
        private const string RelayPartySuiteErrorMessage = "Azure App Service sandbox component is disabled. This can happen if the App is running on ILB ASE with Relay Party suite service enabled. For this configuration, none of the diagnostic tools work.";

        public HttpResponseMessage Get()
        {
            try
            {
                SessionController sessionController = new SessionController();
                if (!sessionController.IsSandboxAvailable())
                {
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, RelayPartySuiteErrorMessage);
                }

                List<DiagnoserDetails> retVal = new List<DiagnoserDetails>();
                foreach (Diagnoser diagnoser in sessionController.GetAllDiagnosers())
                {
                    retVal.Add(new DiagnoserDetails(diagnoser));
                }

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
