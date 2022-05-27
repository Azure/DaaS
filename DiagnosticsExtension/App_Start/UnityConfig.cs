// -----------------------------------------------------------------------
// <copyright file="UnityConfig.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Web.Http;
using DaaS.Sessions;
using Unity;
using Unity.WebApi;

namespace DiagnosticsExtension
{
    public static class UnityConfig
    {
        public static void RegisterComponents()
        {
            var container = new UnityContainer();
            container.RegisterType<ISessionManager, SessionManager>(TypeLifetime.Singleton);
            GlobalConfiguration.Configuration.DependencyResolver = new UnityDependencyResolver(container);
        }
    }
}
