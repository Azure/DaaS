// -----------------------------------------------------------------------
// <copyright file="SettingsV2Controller.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using DaaS.V2;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("settings")]
    public class SettingsV2Controller : ApiController
    {
        [HttpPost]
        [Route("")]
        public IHttpActionResult Post()
        {
            var settings = new Dictionary<string, object>();
            var settingsType = typeof(Settings);
            var properties = settingsType.GetProperties()
                .Where(prop => prop.CanRead 
                && !prop.PropertyType.IsArray);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(Settings.Instance, null);
                settings[prop.Name] = value;
            }

            return Ok(settings);
        }
    }
}
