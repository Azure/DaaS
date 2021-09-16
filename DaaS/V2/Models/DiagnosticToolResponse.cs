// -----------------------------------------------------------------------
// <copyright file="DiagnosticToolResponse.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace DaaS.V2
{
    public class DiagnosticToolResponse
    {
        internal List<LogFile> Logs { get; set; } = new List<LogFile>();
        internal List<string> Errors { get; set; } = new List<string>();
    }
}