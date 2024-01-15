// -----------------------------------------------------------------------
// <copyright file="HyperVUtils.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS
{
    public static class HyperVUtils
    {
        public static bool IsHyperV()
        {
            string isolationMode = Environment.GetEnvironmentVariable("WEBSITE_ISOLATION");
            return (!string.IsNullOrWhiteSpace(isolationMode) && isolationMode.Equals("hyperv", StringComparison.CurrentCultureIgnoreCase));
        }
    }
}
