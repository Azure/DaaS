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
            config.Routes.MapHttpRoute(
                name: "SettingsApi",
                routeTemplate: "api/settings",
                defaults: new { controller = "Settings" }
            );

            config.Routes.MapHttpRoute(
                name: "DiagnosersApi",
                routeTemplate: "api/diagnosers",
                defaults: new { controller = "Diagnosers" }
            );

            config.Routes.MapHttpRoute(
                name: "InstancesApi",
                routeTemplate: "api/instances",
                defaults: new { controller = "Instances" }
            );

            //config.Routes.MapHttpRoute(
            //    name: "SessionsApi",
            //    routeTemplate: "api/sessions/{type}/{detailed}",
            //    defaults: new { controller = "Sessions", type = "all", detailed = RouteParameter.Optional }
            //);

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

            //config.Routes.MapHttpRoute(
            //    name: "DefaultApi",
            //    routeTemplate: "api/{controller}/{id}",
            //    defaults: new { id = RouteParameter.Optional }
            //);

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
