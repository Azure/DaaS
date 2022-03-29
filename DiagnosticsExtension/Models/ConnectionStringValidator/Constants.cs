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
        public const string ManagedIdentityTutorial = "A relevant tutorial is also available <a href='https://docs.microsoft.com/azure/azure-functions/functions-identity-based-connections-tutorial' target='_blank'>here</a>.";
        public const string BlobServiceUriMissingSummary = "Necessary connection information was not found";
        public const string BlobServiceUriMissingDetails = "To connect to your storage account, you need to provide a connection string or use identity-based connections.  To learn more please refer <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#connections>' target='_blank'>here</a>. " + ManagedIdentityTutorial;
        public const string BlobServiceUriEmptySummary = "The app setting {0} has an empty value.";
        public const string BlobServiceUriEmptyDetails = "Refer <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>here</a> for relevant configuration details. " + ManagedIdentityTutorial;
        public const string ServiceUriEmpty = "ServiceUriEmpty";
        public const string QueueServiceUriMissingSummary = "The app setting {0}__queueServiceUri is not configured or has an empty value.";
        public const string QueueServiceUriMissingDetails = "Refer <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>here</a> for relevant configuration details. " + ManagedIdentityTutorial;
        public const string QueueServiceUri = "__queueServiceUri";
        public const string ValidCredentialValue = "managedidentity";
        public const string FullyQualifiedNamespace = "__fullyQualifiedNamespace";
        public const string ManagedIdentityClientIdNullOrEmptySummary = "The app setting {0}" + ClientId + " was expected but not found.";
        public const string ManagedIdentityClientIdNullOrEmptyDetails = "When the app setting {0}" + Credential + " is configured to \"managedidentity\", the clientId of a user assigned managed identity assigned to the Function App is expected in the app setting {0}" + ClientId + ".  Refer <a href='https://docs.microsoft.com/azure/azure-functions/functions-reference#common-properties-for-identity-based-connections' target='_blank'>here</a> for details. " + ManagedIdentityTutorial;
        public const string AuthorizationFailure = "Authorization failure";
        public const string AuthenticationFailure = "Authentication failure";
        public const string InvalidConnection = "Invalid connection string";
        public const string ResourceNotFound = "Resource not found";
        public const string ClientIdInvalidTokenGeneratedSummary = "The ClientId configured in the app setting {0}" + ClientId + " does not match any user assigned managed identity assigned to this app.";
        public const string ClientIdInvalidTokenGeneratedDetails = "Refer <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>here</a> for details related to configuring connections via managed identity. " + ManagedIdentityTutorial;
        public const string SystemAssignedAuthFailure = "The system assigned managed identity for this Function App does not have access to the resource configured in {0}.  Refer <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>here</a> for details around configuring connections via managed identity. " + ManagedIdentityTutorial;
        public const string UserAssignedAuthFailure = "The configured user assigned managed identity does not have access to the resource configured in {0}.  Refer <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>here</a> for details around configuring connections via managed identity. " + ManagedIdentityTutorial;
        
        public const string AuthFailureDetails = "Authentication failure -the credentials in the configured connection string are either invalid or expired. Please update the app setting with a valid connection string.";
        public const string DnsLookupFailedDetails = "The service resource specified in the connection string was not found. Please check the value of the setting.";
        public const string FQNamespaceResourceNotFound = "The resource specified in the app setting {0}__fullyQualifiedNamespace was not found. Please check the value of the setting.";
        public const string StorageAccountResourceNotFound = "The resource specified in the app setting {0} was not found. Please check the value of the setting.";
        public const string MalformedConnectionStringDetails = "The connection string configured is invalid(e.g.missing some required elements). Please check the value configured in the app setting ";
        public const string ManagedIdentityCredentialInvalidSummary = "The app setting {0}" + Credential + " is defined but not valid.  To configure user assigned managed identity, set it's value to \"managedidentity\".";
        public const string ManagedIdentityCredentialInvalidDetails = "Refer <a href='https://docs.microsoft.com/azure/azure-functions/functions-reference#common-properties-for-identity-based-connections' target='_blank'>here</a> for more information about configuring this app setting. " + ManagedIdentityTutorial;
        #endregion
    }
}
