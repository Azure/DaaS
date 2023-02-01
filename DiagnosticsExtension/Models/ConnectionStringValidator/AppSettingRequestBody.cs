// -----------------------------------------------------------------------
// <copyright file="AppSettingRequestBody.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class AppSettingRequestBody
    {
        public string AppSettingName { get; set; }
        public string Type { get; set; }
    }
}
