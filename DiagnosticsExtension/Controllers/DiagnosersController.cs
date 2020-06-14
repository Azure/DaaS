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
        public IEnumerable<DiagnoserDetails> Get()
        {
            List<DiagnoserDetails> retVal = new List<DiagnoserDetails>();

            try
            {
                SessionController sessionController = new SessionController();

                foreach (Diagnoser diagnoser in sessionController.GetAllDiagnosers())
                {
                    retVal.Add(new DiagnoserDetails(diagnoser));
                }
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while getting diagnosers", ex);
                throw;
            }
            
            return retVal;
        }

        //// POST api/values
        //public void Post([FromBody]string value)
        //{
        //}

        //// PUT api/values/5
        //public void Put(int id, [FromBody]string value)
        //{
        //}

        //// DELETE api/values/5
        //public void Delete(int id)
        //{
        //}
    }
}
