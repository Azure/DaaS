// -----------------------------------------------------------------------
// <copyright file="Global.asax.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace DiagnosticsExtension
{
    // Note: For instructions on enabling IIS6 or IIS7 classic mode, 
    // visit http://go.microsoft.com/?LinkId=9394801

    public class WebApiApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            var appStartedCalledAt = DateTime.UtcNow;
            EnsureHomeEnvironmentVariable();
            AreaRegistration.RegisterAllAreas();

            UnityConfig.RegisterComponents();

            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            GlobalConfiguration.Configuration.Formatters.Clear();
            GlobalConfiguration.Configuration.Formatters.Add(new System.Net.Http.Formatting.JsonMediaTypeFormatter());
            GlobalConfiguration.Configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
            GlobalConfiguration.Configuration.Formatters.JsonFormatter.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore;

            GlobalConfiguration.Configuration.MessageHandlers.Add(new LoggingHandler());
            
            Task.Run(() =>
            {
                try
                {
                    SessionController sessionController = new SessionController();
                    sessionController.StartSessionRunner();
                }
                catch (Exception)
                {
                }
            });

            DaaS.Logger.LogVerboseEvent($"DAAS Application_Start took {DateTime.UtcNow.Subtract(appStartedCalledAt).TotalMilliseconds} ms");
        }

        internal void EnsureHomeEnvironmentVariable()
        {
            //For Debug
            if (HostingEnvironment.IsDevelopmentEnvironment)
            {
                Environment.SetEnvironmentVariable("HOME", @"c:\temp\daas");
                if (!Directory.Exists(Environment.ExpandEnvironmentVariables(@"%HOME%")))
                {
                    Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(@"%HOME%"));
                }
            }
        }
    }
}
