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
            else if (e is EmptyConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
            }
            else if (e.InnerException != null &&
                     e.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
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
