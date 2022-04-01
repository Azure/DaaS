// -----------------------------------------------------------------------
// <copyright file="ManagedIdentityConnectionResponseUtility.cs" company="Microsoft Corporation">
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
    public static class ManagedIdentityConnectionResponseUtility
    {
        public static void EvaluateResponseStatus(Exception e, ConnectionStringType type, ref ConnectionStringValidationResult response, string appSettingName = "")
        {
            if (e is ManagedIdentityException)
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityConnectionFailed;
                response.StatusSummary = ((ManagedIdentityException)e).MessageSummary;
                response.StatusDetails = ((ManagedIdentityException)e).MessageDetails;
            }
            else if (e is UnauthorizedAccessException && e.Message.Contains("unauthorized") || e.Message.Contains("Unauthorized") || e.Message.Contains("request is not authorized"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityAuthFailure;
                response.StatusSummary = Constants.AuthorizationFailure;
                if (response.IdentityType == "System")
                {
                    response.StatusDetails = String.Format(Constants.SystemAssignedAuthFailure, GetTargetConnectionAppSettingName(type, appSettingName));
                }
                else
                {
                    response.StatusDetails = String.Format(Constants.UserAssignedAuthFailure, GetTargetConnectionAppSettingName(type, appSettingName));
                }

                response.Exception = e;
            }
            else if (e is AuthenticationFailedException && e.Message.Contains("ManagedIdentityCredential"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.ManagedIdentityNotConfigured;
                response.StatusSummary = "Your app is configured to use identity based connection but does not have a system assigned managed identity assigned. Refer <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-reference#configure-an-identity-based-connection' target='_blank'>here</a> for details.";
            }
            else if (e.Message.Contains("fullyQualifiedNamespace"))
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.FullyQualifiedNamespaceMissing;
                response.StatusSummary = "The app setting " + appSettingName + "__fullyQualifiedNamespace was not found or is set to a blank value. Refer <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-reference#configure-an-identity-based-connection' target='_blank'>here</a> for details.";
            }
            else if (e.InnerException != null &&
                     e.InnerException.Message.Contains("The remote name could not be resolved")) // queue and blob
            {

                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.StatusSummary = String.Format(Constants.StorageAccountResourceNotFound, GetTargetConnectionAppSettingName(type, appSettingName));
            }
            else if (e.Message.Contains("No such host is known")) // event hub and service bus
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                response.StatusSummary = Constants.ResourceNotFound;
                response.StatusDetails = String.Format(Constants.FQNamespaceResourceNotFound, appSettingName);
                response.Exception = e;
            }
            else
            {
                response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                response.Exception = e;
            }
        }
        public static string ResolveManagedIdentityCommonProperty(string appSettingName, ConnectionStringValidationResult.ManagedIdentityCommonProperty prop)
        {
            string commonPropertyValue = Environment.GetEnvironmentVariable(appSettingName + Constants.UnderscoreSeperator + prop.ToString());

            if (commonPropertyValue == null)
            {
                commonPropertyValue = Environment.GetEnvironmentVariable(appSettingName + Constants.ColonSeparator + prop.ToString());
            }
            return commonPropertyValue;
        }
        public static string GetTargetConnectionAppSettingName(ConnectionStringType type, string appSettingName)
        {
            string serviceuriAppSettingName = "";
            switch (type)
            {

                case ConnectionStringType.ServiceBus:
                case ConnectionStringType.EventHubs:
                    serviceuriAppSettingName = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith(appSettingName) && k.ToLower().EndsWith("fullyqualifiednamespace")).FirstOrDefault();

                    break;
                case ConnectionStringType.BlobStorageAccount:
                case ConnectionStringType.QueueStorageAccount:
                    serviceuriAppSettingName = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith(appSettingName) && k.ToLower().EndsWith("serviceuri")).FirstOrDefault();

                    break;
            }
            return serviceuriAppSettingName;
        }
    }
}
