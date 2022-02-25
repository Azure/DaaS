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
using Microsoft.WindowsAzure.Storage;

namespace DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions
{
    public static class ConnectionStringResponseUtility
    {
        #region string constants for network validator
        public const string ClientId = "__clientId";
        public const string Credential = "__credential";
        public const string ServiceUriMissed = "ServiceUriMissed";
        public const string ValidCredentialValue = "managedidentity";
        public const string FullyQualifiedNamespace = "__fullyQualifiedNamespace";
        public const string ManagedIdentityCredentialMissing = "ManagedIdentityCredentialMissing";
        #endregion

        public static ConnectionStringValidationResult EvaluateResponseStatus(Exception e, ConnectionStringType type, ConnectionStringValidationResult.ManagedIdentityType identityType)
        {
            var response = new ConnectionStringValidationResult(type);
            if (e is MalformedConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
            }
            else if (e.Message.Contains("managedIdentityCredentialMissing"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityCredentialMissing;
            }
            else if (e.Message.Contains("Unauthorized") || e.Message.Contains("AuthorizationPermissionMismatch"))
            {
                if (identityType == ConnectionStringValidationResult.ManagedIdentityType.User)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UserAssignedManagedIdentity;
                }
                else
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.SystemAssignedManagedIdentity;
                }
            }
            else if (e.Message.Contains("ManagedIdentityCredential"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityCredential;
            }
            else if (e.Message.Contains("fullyQualifiedNamespace"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.FullyQualifiedNamespaceMissed;
            }
            else if (e.Message.Contains("ServiceUriMissed"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ServiceUriMissed;
            }
            else if (e is EmptyConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
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
            else if (e.Message.Contains("InvalidSignature") ||
                         e.Message.Contains("Unauthorized"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
            }
            else if ((e is ArgumentException && e.Message.Contains("Authentication ")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature") ||
                         e.Message.Contains("Unauthorized"))
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

            return response;
        }
    }
}
