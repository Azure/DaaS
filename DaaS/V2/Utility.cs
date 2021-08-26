// -----------------------------------------------------------------------
// <copyright file="Utility.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS.V2
{
    internal class Utility
    {
        /// <summary>
        /// The functionality in Antares to get the HostName is a bit flaky. This method is used mostly to construct
        /// the paths of logs and reports in the session and if we fail to identify the hostname properly, we will
        /// return empty and let the client (UI or CLI) append the SCM hostname to the URL
        /// </summary>
        /// <returns>The default hostname of the site or an empty string</returns>
        public static string GetScmHostName()
        {
            var scmHostName = Settings.Instance.DefaultScmHostName;
            if (!string.IsNullOrEmpty(scmHostName)
                && scmHostName.Contains(".scm."))
            {
                return $"https://{Settings.Instance.DefaultScmHostName}";
            }

            return string.Empty;
        }
    }
}
