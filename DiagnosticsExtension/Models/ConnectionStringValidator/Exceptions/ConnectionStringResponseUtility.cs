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
        public static void EvaluateResponseStatus(Exception e, ConnectionStringType type, ref ConnectionStringValidationResult response, string appSettingName = "")
        {
            if (e is MalformedConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                response.StatusSummary = String.Format(Constants.MalformedConnectionStringDetails, appSettingName);
                response.StatusDetails = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e is EmptyConnectionStringException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                response.StatusSummary = "The app setting " + appSettingName + " was not found or is set to a blank value";
                response.Exception = e;
            }
            else if (e is UnauthorizedAccessException && e.Message.Contains("unauthorized") || e.Message.Contains("Unauthorized") || e.Message.Contains("request is not authorized"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.StatusSummary = Constants.AuthenticationFailure;
                response.StatusDetails = Constants.AuthFailureDetails;
                response.Exception = e;
            }
            else if (e.InnerException != null &&
                     e.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.StatusSummary = Constants.DnsLookupFailed;
                response.Exception = e;
            }
            else if (e.InnerException != null && e.InnerException.InnerException != null &&
                        e.InnerException.InnerException.Message.Contains("The remote name could not be resolved"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.StatusSummary = Constants.DnsLookupFailed;
                response.Exception = e;
            }
            else if (e.Message.Contains("No such host is known"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.StatusSummary = Constants.DnsLookupFailed;
                response.Exception = e;
            }
            else if (e is ArgumentNullException ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("was not found"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                response.StatusSummary = String.Format(Constants.MalformedConnectionStringDetails, appSettingName);
                response.StatusDetails = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e is ArgumentException && e.Message.Contains("entityPath is null") ||
                         e.Message.Contains("HostNotFound") ||
                         e.Message.Contains("could not be found") ||
                         e.Message.Contains("The argument  is null or white space"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                response.StatusSummary = String.Format(Constants.MalformedConnectionStringDetails, appSettingName);
                response.StatusDetails = Constants.GenericDetailsMessage;
                response.Exception = e;
            }
            else if (e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.StatusSummary = Constants.AuthenticationFailure;
                response.StatusDetails = Constants.AuthFailureDetails;
                response.Exception = e;
            }
            else if ((e is ArgumentException && e.Message.Contains("Authentication")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.StatusSummary = Constants.AuthenticationFailure;
                response.StatusDetails = Constants.AuthFailureDetails;
                response.Exception = e;
            }
            else if ((e is Azure.RequestFailedException && e.Message.Contains("failed to authenticate")) ||
                         e.Message.Contains("claim is empty or token is invalid") ||
                         e.Message.Contains("InvalidSignature"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                response.StatusSummary = Constants.AuthenticationFailure;
                response.StatusDetails = Constants.AuthFailureDetails;
                response.Exception = e;
            }
            else if (e.Message.Contains("Ip has been prevented to connect to the endpoint"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                if (e.Message.Contains("AuthenticationFailed"))
                {
                    response.StatusSummary = Constants.AuthenticationFailure;
                    response.StatusDetails = Constants.AuthFailureDetails;
                }
                else
                {
                    response.StatusSummary = "Access to the "+ type +" resource is restricted.";
                    switch (type)
                    {
                        case ConnectionStringType.ServiceBus:
                            response.StatusDetails = Constants.ServiceBusAccessRestrictedDetails;
                            break;
                        case ConnectionStringType.EventHubs:
                            response.StatusDetails = Constants.EventHubAccessRestrictedDetails;
                            break;
                        case ConnectionStringType.StorageAccount:
                        case ConnectionStringType.BlobStorageAccount:
                        case ConnectionStringType.QueueStorageAccount:
                        case ConnectionStringType.FileShareStorageAccount:
                            response.StatusDetails = Constants.StorageAccessRestrictedDetails;
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
                    response.StatusSummary = Constants.AuthenticationFailure;
                    response.StatusDetails = Constants.AuthFailureDetails;
                }
                else if (((StorageException)e).RequestInformation.HttpStatusCode == 403)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                    if (e.Message.Contains("AuthenticationFailed"))
                    {
                        response.StatusSummary = Constants.AuthenticationFailure;
                        response.StatusDetails = Constants.AuthFailureDetails;
                    }
                    else
                    {
                        response.StatusSummary = "Access to the " + type + " resource is restricted.";
                        switch (type)
                        {
                            case ConnectionStringType.ServiceBus:
                                response.StatusDetails = Constants.ServiceBusAccessRestrictedDetails;
                                break;
                            case ConnectionStringType.EventHubs:
                                response.StatusDetails = Constants.EventHubAccessRestrictedDetails;
                                break;
                            case ConnectionStringType.StorageAccount:
                            case ConnectionStringType.BlobStorageAccount:
                            case ConnectionStringType.QueueStorageAccount:
                            case ConnectionStringType.FileShareStorageAccount:
                                response.StatusDetails = Constants.StorageAccessRestrictedDetails;
                                break;
                        }
                    }
                }
                response.Exception = e;
            }
            else
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                response.Exception = e;
            }
        }

    }
}
