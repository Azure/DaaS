//-----------------------------------------------------------------------
// <copyright file="NetCoreWebConfigHelpers.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace DaaS.ApplicationInfo
{
    public static class NetCoreWebConfigHelpers
    {
        public static FileInfo GetWebConfig()
        {
            var directory = new DirectoryInfo(Path.Combine(Environment.GetEnvironmentVariable("HOME_EXPANDED"), "site", "wwwroot"));
            return directory.GetFiles("web.config").FirstOrDefault();
        }

        public static void ParseAspNetCoreSettings(FileInfo webConfig, out bool isAspNetCore, out bool stdoutLogEnabled, out string stdoutLogFile)
        {
            stdoutLogEnabled = false;
            stdoutLogFile = string.Empty;
            isAspNetCore = false;

            if (webConfig == null)
            {
                LogException(new ArgumentNullException(nameof(webConfig)));
                return;
            }

            var xdocument = XDocument.Load(webConfig.FullName);
            var aspNetCoreHandler = xdocument?.Descendants().Where(p => p.Name.LocalName == "aspNetCore").FirstOrDefault();

            if (aspNetCoreHandler == null)
            {
                LogException(new Exception($"XDocument descendent {"aspNetCore"} not found"));
                return;
            }
            else
            {
                isAspNetCore = true;
                stdoutLogEnabled = bool.Parse((string)aspNetCoreHandler.Attribute("stdoutLogEnabled"));
                stdoutLogFile = (string)aspNetCoreHandler.Attribute("stdoutLogFile");
            }
        }
        
        public static void EditAspNetCoreSetting(FileInfo webConfig, bool stdoutLogEnabled)
        {
            if (webConfig == null)
                throw LogException(new ArgumentNullException(nameof(webConfig)));
            
            string aspNetCoreTag = "aspNetCore";
            string stdoutLogSetting = "stdoutLogEnabled";

            var xdocument = XDocument.Load(webConfig.FullName);

            var handler = xdocument?.Descendants().Where(p => p.Name.LocalName.Equals(aspNetCoreTag, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
            if (handler == null)
                throw LogException(new Exception($"XDocument descendent {aspNetCoreTag} not found"));

            var targetAttribute = handler?.Attribute(stdoutLogSetting);
            if (targetAttribute == null)
                throw LogException(new Exception($"aspNetCore setting {stdoutLogSetting} not found"));
            
            targetAttribute.SetValue(stdoutLogEnabled.ToString());
            xdocument.Save(webConfig.FullName);
        }

        private static Exception LogException(Exception ex, string logMessage = "")
        {
            if (string.IsNullOrWhiteSpace(logMessage))
                logMessage = ex.Message;

            Logger.LogErrorEvent(logMessage, ex);

            return ex;
        }
    }
}
