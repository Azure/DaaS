//-----------------------------------------------------------------------
// <copyright file="DiagnosersController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using DaaS.Diagnostics;
using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class DiagnosersController : ApiController
    {
        public HttpResponseMessage Get()
        {
            try
            {
                List<DiagnoserDetails> retVal = new List<DiagnoserDetails>();
                SessionController sessionController = new SessionController();
                foreach (Diagnoser diagnoser in sessionController.GetAllDiagnosers())
                {
                    retVal.Add(new DiagnoserDetails(diagnoser));
                }
                return Request.CreateResponse(HttpStatusCode.OK, retVal);
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while getting diagnosers", ex);
                return Request.CreateErrorResponse(HttpStatusCode.OK, ex.Message);
            }
        }
    }
}
