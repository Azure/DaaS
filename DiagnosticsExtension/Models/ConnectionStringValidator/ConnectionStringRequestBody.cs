//-----------------------------------------------------------------------
// <copyright file="ConnectionStringRequestBody.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class ConnectionStringRequestBody
    {
        public string ConnectionString { get; set; }
        public string Type { get; set; }
    }
}