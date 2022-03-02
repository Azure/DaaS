// -----------------------------------------------------------------------
// <copyright file="ConnectionStringResponseUtility.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Azure;
using Azure.Identity;
using Microsoft.WindowsAzure.Storage;

namespace DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions
{
    public static class ConnectionStringResponseUtility
    {
        #region string constants for network validator
        public const string User = "User";
        public const string System = "System";
        public const string ClientId = "__clientId";
        public const string Credential = "__credential";
        public const string ServiceUri = "__serviceUri";
        public const string BlobServiceUri = "__blobServiceUri";
        public const string ServiceUriMissing = "ServiceUriMissing";
        public const string QueueServiceUri = "__queueServiceUri";
        public const string ValidCredentialValue = "managedidentity";
        public const string FullyQualifiedNamespace = "__fullyQualifiedNamespace";
        public const string ManagedIdentityCredentialMissing = "ManagedIdentityCredentialMissing";
        #endregion

        public static void EvaluateResponseStatus(Exception e, ConnectionStringType type, ref ConnectionStringValidationResult response)
        {
            if (e is MalformedConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
            }
            else if (e is EmptyConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
            }
            else if (e.Message.Contains(ManagedIdentityCredentialMissing))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityCredentialMissing;
            }
            else if (e.Message.Contains("Unauthorized") || e.Message.Contains("unauthorized") || e.Message.Contains("request is not authorized"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityAuthFailure;
            }
            else if (e is AuthenticationFailedException && e.Message.Contains("ManagedIdentityCredential"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityNotConfigured;
            }
            else if (e.Message.Contains("fullyQualifiedNamespace"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.FullyQualifiedNamespaceMissing;
            }
            else if (e.Message.Contains("ServiceUriMissing"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ServiceUriMissing;
            }
            else if (e.InnerException != null &&
                     e.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
            }
            else if (e.InnerException != null && e.InnerException.InnerException != null &&
                        e.InnerException.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
            }
            else if (e.Message.Contains("No such host is known"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
            }
            else if (e is ArgumentNullException ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("was not found"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
            }
            else if (e is ArgumentException && e.Message.Contains("entityPath is null") ||
                         e.Message.Contains("HostNotFound") ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("The argument  is null or white space"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
            }
            else if (e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
            }
            else if ((e is ArgumentException && e.Message.Contains("Authentication")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
            }
            else if (e.Message.Contains("Ip has been prevented to connect to the endpoint"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
            }
            else if (e is StorageException)
            {
                if (((StorageException)e).RequestInformation.HttpStatusCode == 401)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                }
                else if (((StorageException)e).RequestInformation.HttpStatusCode == 403)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                }
            }
            else
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
            }
            response.Exception = e;
        }
    }
}
