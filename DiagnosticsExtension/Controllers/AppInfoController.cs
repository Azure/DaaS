// -----------------------------------------------------------------------
// <copyright file="AppInfoController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaaS.ApplicationInfo;

namespace DiagnosticsExtension.Controllers
{
    public class AppInfoController : ApiController
    {
        public HttpResponseMessage Get()
        {
            try
            {
                AppModelDetector detector = new AppModelDetector();
                var version = detector.Detect(new DirectoryInfo(Path.Combine(Environment.GetEnvironmentVariable("HOME_EXPANDED"), "site", "wwwroot")));
                return Request.CreateResponse(HttpStatusCode.OK, version);
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while checking appinfo", ex);
                return Request.CreateErrorResponse(HttpStatusCode.OK, ex.Message);
            }
        }
    }
}
