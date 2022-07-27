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
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public static class ConnectionStringResponseUtility
    {
        public static void EvaluateResponseStatus(Exception e, ConnectionStringType type, ref ConnectionStringValidationResult response, string appSettingName = "")
        {
            // Check if the value is a key vault reference that failed to resolve to the connection string by platform
            if (ConnectionStringResponseUtility.IsKeyVaultReference(Environment.GetEnvironmentVariable(appSettingName)))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.KeyVaultReferenceResolutionFailed;
                response.Summary = String.Format(Constants.KeyVaultReferenceResolutionFailedSummary, appSettingName);
            }
            else if (e is MalformedConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                response.Summary = String.Format(Constants.MalformedConnectionStringDetails, appSettingName);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e is EmptyConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                response.Summary = "The app setting " + appSettingName + " was not found or is set to a blank value";
                response.Exception = e;
            }
            else if (e is UnauthorizedAccessException && e.Message.Contains("unauthorized") || e.Message.Contains("Unauthorized") || e.Message.Contains("request is not authorized"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e.InnerException != null &&
                     e.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.Summary = Constants.DnsLookupFailed;
                response.Exception = e;
            }
            else if (e.InnerException != null && e.InnerException.InnerException != null &&
                     e.InnerException.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.Summary = Constants.DnsLookupFailed;
                response.Exception = e;
            }
            else if (e.Message.Contains("No such host is known"))
            {
                // Thrown when the endpoint specified (e.g. Service Bus namespace) is not found (DNS resolution fails)
                // Can happen due to misconfiguration or when the resource cannot be discovered as it is is behind a private
                // endpoint not accessible from this network
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.Summary = Constants.DnsLookupFailed;
                response.Exception = e;
            }
            else if (e is ArgumentNullException ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("was not found"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                response.Summary = String.Format(Constants.MalformedConnectionStringDetails, appSettingName);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e is ArgumentException && e.Message.Contains("entityPath is null") ||
                         e.Message.Contains("HostNotFound") ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("The argument  is null or white space"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                response.Summary = String.Format(Constants.MalformedConnectionStringDetails, appSettingName);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if ((e is ArgumentException && e.Message.Contains("Authentication")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if ((e is Azure.RequestFailedException && e.Message.Contains("failed to authenticate")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                response.Details = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e.Message.Contains("Ip has been prevented to connect to the endpoint"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                if (e.Message.Contains("AuthenticationFailed"))
                {
                    response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                    response.Details = Constants.GenericDetailsMessage;
                }
                else
                {
                    response.Summary = "Access to the "+ type +" resource is restricted.";
                    switch (type)
                    {
                        case ConnectionStringType.ServiceBus:
                            response.Details = Constants.ServiceBusAccessRestrictedDetails;
                            break;
                        case ConnectionStringType.EventHubs:
                            response.Details = Constants.EventHubAccessRestrictedDetails;
                            break;
                        case ConnectionStringType.StorageAccount:
                        case ConnectionStringType.BlobStorageAccount:
                        case ConnectionStringType.QueueStorageAccount:
                        case ConnectionStringType.FileShareStorageAccount:
                            response.Details = Constants.StorageAccessRestrictedDetails;
                            break;
                    }
                }
                response.Exception = e;
            }
            else if (e is StorageException)
            {
                if (((StorageException)e).RequestInformation.HttpStatusCode == 401)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                    response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                    response.Details = Constants.GenericDetailsMessage;
                }
                else if (((StorageException)e).RequestInformation.HttpStatusCode == 403)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                    if (e.Message.Contains("AuthenticationFailed"))
                    {
                        response.Summary = String.Format(Constants.AuthFailureSummary, appSettingName) + " " + GetRelevantAuthFailureDocs(type);
                        response.Details = Constants.GenericDetailsMessage;
                    }
                    else
                    {
                        response.Summary = "Access to the " + type + " resource is restricted.";
                        switch (type)
                        {
                            case ConnectionStringType.ServiceBus:
                                response.Details = Constants.ServiceBusAccessRestrictedDetails;
                                break;
                            case ConnectionStringType.EventHubs:
                                response.Details = Constants.EventHubAccessRestrictedDetails;
                                break;
                            case ConnectionStringType.StorageAccount:
                            case ConnectionStringType.BlobStorageAccount:
                            case ConnectionStringType.QueueStorageAccount:
                            case ConnectionStringType.FileShareStorageAccount:
                                response.Details = Constants.StorageAccessRestrictedDetails;
                                break;
                        }
                    }
                }
                response.Exception = e;
            }
            else
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                response.Summary = Constants.UnknownErrorSummary;
                response.Exception = e;
            }
        }
        public static bool IsKeyVaultReference(string value)
        {
            return value.Contains("@Microsoft.KeyVault");
        }

        private static string GetRelevantAuthFailureDocs(ConnectionStringType type)
        {
            switch (type)
            {
                case ConnectionStringType.BlobStorageAccount:
                    return Constants.AuthFailureBlobStorageDocs;
                case ConnectionStringType.QueueStorageAccount:
                    return Constants.AuthFailureQueueStorageDocs;
                case ConnectionStringType.FileShareStorageAccount:
                    return Constants.AuthFailureFileShareStorageDocs;
                case ConnectionStringType.ServiceBus:
                    return Constants.AuthFailureServiceBusDocs;
                case ConnectionStringType.EventHubs:
                    return Constants.AuthFailureEventHubsDocs;
                default:
                    return "";
            }
        }
    }
}
