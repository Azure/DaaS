// -----------------------------------------------------------------------
// <copyright file="ConnectionStringValidationResult.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Web;
using System.Web.UI.WebControls;
using Newtonsoft.Json;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{

    public class ConnectionStringValidationResult
    {
        [Newtonsoft.Json.JsonIgnore]
        public ResultStatus? Status;
        [Newtonsoft.Json.JsonIgnore]
        public string IdentityType;
        [JsonProperty("Summary")]
        public string StatusSummary;
        [JsonProperty("Details")]
        public string StatusDetails;
        public string StatusText => Status?.ToString();
        [Newtonsoft.Json.JsonIgnore]
        public Exception Exception;
        public string ExceptionMessage => Exception?.Message;
        [Newtonsoft.Json.JsonIgnore]
        public object Payload;
        [Newtonsoft.Json.JsonIgnore]
        public string Type => type.ToString();

        private ConnectionStringType type;

        public ConnectionStringValidationResult(ConnectionStringType type)
        {
            this.type = type;
        }
        public enum ResultStatus
        {
            Success,
            AuthFailure,
            ContentNotFound,
            Forbidden,
            UnknownResponse,
            EndpointNotReachable,
            ConnectionFailure,
            DnsLookupFailed,
            MsiFailure,
            EmptyConnectionString,
            MalformedConnectionString,
            EntityNotFound,
            FullyQualifiedNamespaceMissing,
            ManagedIdentityNotConfigured,
            ManagedIdentityAuthFailure,
            ManagedIdentityConnectionFailed,
            KeyVaultReferenceResolutionFailed,
            UnknownError,
        }
        public enum ManagedIdentityCommonProperty
        {
            fullyQualifiedNamespace,
            credential,
            clientId,
            serviceUri,
            blobServiceUri,
            queueServiceUri
        }


    }
}
