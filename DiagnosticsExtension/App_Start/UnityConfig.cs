﻿// -----------------------------------------------------------------------
// <copyright file="UnityConfig.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Web.Http;
using DaaS.Sessions;
using DaaS.Storage;
using Unity;
using Unity.WebApi;

namespace DiagnosticsExtension
{
    public static class UnityConfig
    {
        public static void RegisterComponents()
        {
            var container = new UnityContainer();
            string isolationMode = Environment.GetEnvironmentVariable("WEBSITE_ISOLATION");
            if (!string.IsNullOrWhiteSpace(isolationMode) && isolationMode.Equals("hyperv", StringComparison.CurrentCultureIgnoreCase))
            {
                container.RegisterType<ISessionManager, HyperVSessionManager>(TypeLifetime.Singleton);
            }
            else
            {
                container.RegisterType<IStorageService, AzureStorageService>(TypeLifetime.Singleton);
                container.RegisterType<ISessionManager, SessionManager>(TypeLifetime.Singleton);
                container.RegisterType<IAzureStorageSessionManager,  AzureStorageSessionManager>(TypeLifetime.Singleton);
            }
            GlobalConfiguration.Configuration.DependencyResolver = new UnityDependencyResolver(container);
        }
    }
}
