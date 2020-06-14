//-----------------------------------------------------------------------
// <copyright file="WebApiConfig.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace DiagnosticsExtension
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            /// Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "PrivateSettingsApi",
                routeTemplate: "api/privatesettings/{name}",
                defaults: new { controller = "PrivateSettings", name = RouteParameter.Optional }
            );


            config.Routes.MapHttpRoute(
                name: "SessionsApi",
                routeTemplate: "api/sessions/{type}/{detailed}",
                defaults: new { controller = "Sessions", type = RouteParameter.Optional, detailed = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "SessionDiagnoserApi",
                routeTemplate: "api/session/{sessionId}/diagnosers/{diagnoser}/{detailed}",
                defaults: new { controller = "SessionDiagnoser", detailed = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "SessionsDiagnosersApi",
                routeTemplate: "api/session/{sessionId}/diagnosers/{detailed}",
                defaults: new { controller = "SessionDiagnosers", detailed = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "SessionApi",
                routeTemplate: "api/session/{sessionId}/{detailed}",
                defaults: new { controller = "SingleSession", detailed = RouteParameter.Optional }
            );

            config.Routes.MapHttpRoute(
                name: "DownloadFilesList",
                routeTemplate: "api/v2/session/downloadfileslist/{sessionId}/{downloadableFileType}",
                defaults: new { controller = "DownloadFilesList" }
            );

            config.Routes.MapHttpRoute(
                name: "DownloadFile",
                routeTemplate: "api/v2/session/downloadfile/{sessionId}/{downloadableFileType}/{diagnoserName}",
                defaults: new { controller = "DownloadFile" }
            );

            config.Routes.MapHttpRoute(
                name: "DaaSVersion",
                routeTemplate: "api/v2/daasversion",
                defaults: new { controller = "DaaSVersion" }
            );

            config.Routes.MapHttpRoute(
                name: "SessionErrors",
                routeTemplate: "api/v2/session/errors/{sessionId}",
                defaults: new { controller = "SessionErrors" }
            );

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            // Uncomment the following line of code to enable query support for actions with an IQueryable or IQueryable<T> return type.
            // To avoid processing unexpected or malicious queries, use the validation settings on QueryableAttribute to validate incoming queries.
            // For more information, visit http://go.microsoft.com/fwlink/?LinkId=279712.
            //config.EnableQuerySupport();

            // To disable tracing in your application, please comment out or remove the following line of code
            // For more information, refer to: http://www.asp.net/web-api
            config.EnableSystemDiagnosticsTracing();
        }
    }
}
