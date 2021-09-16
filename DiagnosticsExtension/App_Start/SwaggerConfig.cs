// -----------------------------------------------------------------------
// <copyright file="SwaggerConfig.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Web.Http;
using DiagnosticsExtension;
using Swashbuckle.Application;
using System.Web;
using System.Linq;

[assembly: PreApplicationStartMethod(typeof(SwaggerConfig), "Register")]

namespace DiagnosticsExtension
{
    public class SwaggerConfig
    {
        public static void Register()
        {
            var thisAssembly = typeof(SwaggerConfig).Assembly;

            //GlobalConfiguration.Configuration
            //    .EnableSwagger(c =>
            //        {
            //            c.SingleApiVersion("v1", "DaaS Site Extension");
            //            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            //        })
            //    .EnableSwaggerUi();
        }
    }
}
