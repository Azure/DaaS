// -----------------------------------------------------------------------
// <copyright file="EnvironmentVariables.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;

namespace DaaS
{
    public static class EnvironmentVariables
    {
        private static string _procDumpPath = string.Empty;

        public static string HomePath 
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%");
            }
        }

        public static string Local
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%SystemDrive%\local");
            }
        }

        public static string LocalTemp
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%TEMP%");
            }
        }

        public static string DataPath
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\Data");
            }
        }

        public static string DaasPath
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\Data\DaaS");
            }
        }

        public static string DaasSymbolsPath
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\Data\DaaS\symbols");
            }
        }

        public static string PrivateSiteExtensionPath
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\SiteExtensions\DaaS");
            }
        }

        public static string DaasWebJobDirectory
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\site\Jobs\Continuous\DaaS");
            }
        }

        public static string DaasConsoleDirectory
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\data\DaaS\bin");
            }
        }

        public static string DaasRunner
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\site\Jobs\Continuous\DaaS\DaasRunner.exe");
            }
        }

        public static string DaasConsole
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\data\DaaS\bin\DaasConsole.exe");
            }
        }

        public static string DaasWebJobAppData
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%HOME%\site\wwwroot\App_Data\Jobs\Continuous\DaaS");
            }
        }

        public static string WebConfigFilePath
        {
            get
            {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME_EXPANDED"), "site", "wwwroot", "web.config");
            }
        }

        public static string WebConfigDirectoryPath
        {
            get
            {
                return Path.Combine(Environment.GetEnvironmentVariable("HOME_EXPANDED"), "site", "wwwroot");
            }
        }

        public static string NdpCorePdbPath
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%SystemDrive%\NdpCorePdb");
            }
        }

        public static string ProcdumpPath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_procDumpPath))
                {
                    var diagnosticToolsPath = Infrastructure.Settings.GetDiagnosticToolsPath();
                    _procDumpPath = Path.Combine(diagnosticToolsPath, "procdump.exe");
                }

                return _procDumpPath;
            }
        }

        public static string PowershellExePath
        {
            get
            {
                return Environment.ExpandEnvironmentVariables(@"%SystemDrive%\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
            }
        }
    }
}
