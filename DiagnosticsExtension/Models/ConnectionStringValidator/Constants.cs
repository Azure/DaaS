// -----------------------------------------------------------------------
// <copyright file="Constants.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public static class Constants
    {
        #region string constants for network validator
        public const string User = "User";
        public const string System = "System";
        public const string UnderscoreSeperator = "__";
        public const string ColonSeparator = ":";
        public const string ClientId = "__clientId";
        public const string Credential = "__credential";
        public const string ServiceUri = "__serviceUri";
        public const string BlobServiceUri = "__blobServiceUri";
        public const string BlobServiceUriMissing = "Neither {0}__blobServiceUri nor {0}__serviceUri app settings were found. <a href= 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob-trigger?tabs=in-process%2Cextensionv5&pivots=programming-language-csharp#identity-based-connections' target='_blank'>Click here to know more</a>";
        public const string BlobServiceUriEmpty = "{0} app setting is empty. <a href= 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob-trigger?tabs=in-process%2Cextensionv5&pivots=programming-language-csharp#identity-based-connections' target='_blank'>Click here to know more</a>";
        public const string ServiceUriEmpty = "ServiceUriEmpty";
        public const string QueueServiceUriMissing = "The {0}__queueServiceUri was not found or is set to a blank value. <a href= 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-blob-trigger?tabs=in-process%2Cextensionv5&pivots=programming-language-csharp#identity-based-connections' target='_blank'>Click here to know more</a>";
        public const string QueueServiceUri = "__queueServiceUri";
        public const string ValidCredentialValue = "managedidentity";
        public const string FullyQualifiedNamespace = "__fullyQualifiedNamespace";
        public const string ManagedIdentityClientIdNullorEmpty = "The {0}__clientId was not found or is set to a blank value.<a href = 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-reference?tabs=blob#common-properties-for-identity-based-connections' target = '_blank' > Click here to know more</a>";
        public const string AuthorizationFailure = "Authorization failure";
        public const string AuthenticationFailure = "Authentication failure";
        public const string ConnectionInformation = "Necessary connection information was not found";
        public const string InvalidConnection = "Invalid connection string";
        public const string ResourceNotFound = "Resource not found";
        public const string DetailsHeader = "Please see exception below. ";
        public const string ClientIdInvalidTokenGeneratedResponse = "ClientIdInvalidTokenGeneratedResponse";
        public const string ClientIdInvalidTokenGenerated = "An invalid clientId has been provided in {0}__clientId. But managed identity connection was attempted becasuse function app has system assigned identity turned on. ";
        public const string SystemAssignedAuthFailure = "The azure resource mentioned in {0} is not provided with access to the function app using system assigned identity. <a href= 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-reference?tabs=blob#configure-an-identity-based-connection' target='_blank'>Click here to know more</a>";
        public const string UserAssignedAuthFailure = "The azure resource mentioned in {0} is not provided with access to the managed identity resource having clientId specified in appsetting {1}__clientId . <a href= 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-reference?tabs=blob#configure-an-identity-based-connection' target='_blank'>Click here to know more</a>";
        public const string AuthFailureDetails = "Authentication failure -the credentials in the configured connection string are either invalid or expired. Please update the app setting with a valid connection string.";
        public const string DnsLookupFailedDetails = "The service resource specified in the connection string was not found. Please check the value of the setting.";
        public const string FQNamespaceResourceNotFound = "The resource specified in the app setting {0}__fullyQualifiedNamespace was not found. Please check the value of the setting.";
        public const string StorageAccountResourceNotFound = "The resource specified in the app setting {0} was not found. Please check the value of the setting.";
        public const string MalformedConnectionStringDetails = "The connection string configured is invalid(e.g.missing some required elements). Please check the value configured in the app setting ";
        public const string ManagedIdentityCredentialInvalid = "The {0}__credential was not set to 'managedidentity'.<a href = 'https://docs.microsoft.com/en-us/azure/azure-functions/functions-reference?tabs=blob#common-properties-for-identity-based-connections' target = '_blank' > Click here to know more</a>";
        #endregion
    }
}
