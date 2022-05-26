// -----------------------------------------------------------------------
// <copyright file="SasUriResponse.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace DiagnosticsExtension.Models
{
    public class SasUriResponse
    {
        public ExtendedError ExtendedError { get; internal set; }
        public string StorageAccount { get; internal set; }
        public string Exception { get; internal set; }
        public bool IsValid { get; internal set; }
        public bool StorageConnectionStringSpecified { get; internal set; }
        public bool IsValidStorageConnectionString { get; internal set; }
        public string StorageConnectionStringException { get; internal set; }
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
