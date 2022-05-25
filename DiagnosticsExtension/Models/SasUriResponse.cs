// -----------------------------------------------------------------------
// <copyright file="SasUriResponse.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models
{
    public class SasUriResponse
    {
        public ExtendedError ExtendedError { get; set; }
        public string StorageAccount { get; set; }
        public string Exception { get; set; }
        public bool IsValid { get; set; }
    }

    public class ExtendedError
    {
        public int HttpStatusCode { get; set; }
        public string HttpStatusMessage { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public IDictionary<string, string> AdditionalDetails { get; set; }
    }
}
