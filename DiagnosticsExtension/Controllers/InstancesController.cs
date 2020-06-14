//-----------------------------------------------------------------------
// <copyright file="InstancesController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using DaaS;
using DaaS.Diagnostics;
using DaaS.Sessions;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class InstancesController : ApiController
    {
        public IEnumerable<String> Get()
        {
            SessionController sessionController = new SessionController();

            try
            {
               return sessionController.GetAllRunningSiteInstances().Select(p => p.Name);
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Encountered exception while getting instances", ex);
                throw;
            }
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
