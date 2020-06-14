//-----------------------------------------------------------------------
// <copyright file="StdoutLogsController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Timers;
using System.Web.Http;
using DiagnosticsExtension.Models;
using DiagnosticsExtension.Parsers;
using DaaS;
using static DaaS.ApplicationInfo.NetCoreWebConfigHelpers;

namespace DiagnosticsExtension.Controllers
{
    public partial class StdoutLogsController : ApiController
    {
        [HttpGet]
        [Route("api/stdoutlogs")]
        public async Task<IHttpActionResult> Get()
        {
            var logFilePath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), "Logfiles");
            var directory = new DirectoryInfo(logFilePath);

            var results = await LogsParser.GetStdoutLogFilesAsync(maxPayloadBytes: 10 * LogsParser.MB);
            return Ok(results);
        }

        [HttpGet]
        [Route("api/settings/stdout")]
        public IHttpActionResult GetSettings()
        {
            var webConfig = GetWebConfig();
            if (webConfig == null)
                return NotFound();

            ParseAspNetCoreSettings(webConfig, out bool isAspNetCore, out var stdoutLogEnabled, out var _);

            return Ok(new LogsSettings(isAspNetCore, stdoutLogEnabled));
        }

        [HttpPut]
        [Route("api/settings/stdout")]
        public IHttpActionResult PutSettings([FromBody]LogsSettings value, [FromUri]int ttl = 600)
        {
            if (CheckIfEnvironmentVariableExists("WEBSITE_RUN_FROM_PACKAGE", "1") || CheckIfEnvironmentVariableExists("WEBSITE_LOCAL_CACHE_OPTION", "Always"))
            {
                return Content(HttpStatusCode.Conflict, "Either LocalCache is enabled or site is running in RunFromPackage mode so cannot modify web.config");
            }

            var fixedTtl = FixTtlRange(ttl);
            var webConfig = GetWebConfig();

            ParseAspNetCoreSettings(webConfig, out bool isAspNetCore, out var stdoutLogEnabled, out var _);

            var currentSettings = new LogsSettings(isAspNetCore, stdoutLogEnabled);

            if (value.Stdout == currentSettings.Stdout)
                return Ok(currentSettings);

            EditAspNetCoreSetting(webConfig, value.Stdout == LoggingState.Enabled);

            if (value.Stdout == LoggingState.Enabled)
                StartShutoffTimer(fixedTtl);
            
            return Ok(
                new
                {
                    Ttl = value.Stdout == LoggingState.Disabled ? -1 : fixedTtl,
                    Stdout = value.Stdout.ToString()
                });
        }

        private static void StartShutoffTimer(int ttl)
        {
            var shutoffTimer = new Timer(TimeSpan.FromSeconds(ttl).TotalMilliseconds);
            shutoffTimer.Elapsed += OnShutoffEvent;
            shutoffTimer.AutoReset = false;
            shutoffTimer.Enabled = true;
        }

        private static int FixTtlRange(int ttl)
        {
            if (ttl < 60)
                return 60;
            if (ttl > 3600)
                return 3600;

            return ttl;
        }

        private static void OnShutoffEvent(object source, ElapsedEventArgs e)
        {
            try { 
                var webConfig = GetWebConfig();
                if (webConfig == null)
                {
                    Logger.LogErrorEvent("StdoutShutoff: Unable to find web.config during stdout shutoff task", "");
                    return;
                }
            
                EditAspNetCoreSetting(webConfig, false);
            }
            catch(Exception ex)
            {
                Logger.LogErrorEvent("StdoutShutoff: Unable to edit web.config to disable stdout logging", ex);
            }
        }

        private static bool CheckIfEnvironmentVariableExists(string envVar, string envValue)
        {
            bool exists = false;
            var envVarValue = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(envVarValue) 
                && envVarValue.Equals(envValue, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
            }
            return exists;
        }
    }
}
