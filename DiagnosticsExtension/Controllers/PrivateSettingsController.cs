// -----------------------------------------------------------------------
// <copyright file="PrivateSettingsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DiagnosticsExtension.Models;

namespace DiagnosticsExtension.Controllers
{
    public class PrivateSettingsController : ApiController
    {
        public String Post([FromBody] PrivateSetting setting)
        {
            DaaS.Configuration.Settings settings = new DaaS.Configuration.Settings();
            try
            {
                settings.SaveSetting(setting.Name, setting.Value);
                return string.Format("UpdatedSetting{0}:{1}", setting.Name, setting.Value);
            }
            catch
            {
                return string.Format("Unable to update setting {0} value", setting.Name);
            }
        }

        public string Get(string name)
        {
            DaaS.Configuration.Settings settings = new DaaS.Configuration.Settings();
            try
            {
                var res = settings.GetSetting(name);
                return res ?? "notfound";
            }
            catch
            {
                return string.Format("Unable to get setting {0} value", name);
            }
        }
    }
}
