// -----------------------------------------------------------------------
// <copyright file="InstancesController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS;
using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class InstancesController : ApiController
    {
        public IEnumerable<String> Get()
        {
            var sessionController = new DaaS.Sessions.SessionController();

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
    }
}
