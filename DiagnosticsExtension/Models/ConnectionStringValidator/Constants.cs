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
        public const string User = "User";
        public const string System = "System";
        public const string UnderscoreSeperator = "__";
        public const string ColonSeparator = ":";
        public const string ClientId = "__clientId";
        public const string Credential = "__credential";
        public const string QueueServiceUri = "__queueServiceUri";
        public const string ValidCredentialValue = "managedidentity";
        public const string FullyQualifiedNamespace = "__fullyQualifiedNamespace";
        public const string UnknownErrorSummary = "Validation of connection string failed due to an unknown error.";
        public const string GenericDetailsMessage = "Additional error details:";
        public const string ManagedIdentityTutorial = "Refer to this <a href='https://docs.microsoft.com/azure/azure-functions/functions-identity-based-connections-tutorial' target='_blank'>relevant tutorial</a>.";
        public const string BlobServiceUriMissingSummary = "Necessary connection settings not found. A connection string or identity-based connection settings are required.  See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#connections>' target='_blank'>relevant docs</a>. ";
        public const string QueueServiceUriMissingSummary = "Necessary connection settings not found. A connection string or identity-based connection settings are required.  See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-queue-trigger#connections>' target='_blank'>relevant docs</a>. ";
        public const string ServiceBusFQMissingSummary = "Necessary connection settings not found. A connection string or identity-based connection settings are required.  See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger#connections>' target='_blank'>relevant docs</a>. ";
        public const string EventHubFQMissingSummary = "Necessary connection settings not found. A connection string or identity-based connection settings are required.  See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-event-hubs-trigger#connections>' target='_blank'>relevant docs</a>. ";
        public const string BlobServiceUriEmptySummary = "The app setting '{0}' has no value. See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string QueueServiceUriEmptySummary = "The app setting '{0}' has no value. See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-queue-trigger#identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string ServiceBusFQNSEmptySummary = "The app setting '{0}' has no value. See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger#identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string EventHubFQNSEmptySummary = "The app setting '{0}' has no value. See <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-event-hubs-trigger#identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string ManagedIdentityClientIdEmptySummary = "The app setting '{0}' has no value.";
        public const string ManagedIdentityClientIdEmptyDetails = "When the app setting '{0}'" + Credential + " is configured to \"managedidentity\", the clientId of a user assigned managed identity assigned to the Function App is expected in the app setting {0}" + ClientId + ". See <a href='https://docs.microsoft.com/azure/azure-functions/functions-reference#common-properties-for-identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string AuthorizationFailure = "Authorization failure";
        public const string AuthenticationFailure = "Authentication failure";
        public const string ClientIdInvalidTokenGeneratedSummary = "The value of the app setting '{0}'" + ClientId + " does not match any user assigned managed identity assigned to this app. See<a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'> relevant docs</a>. " + ManagedIdentityTutorial;
        public const string SystemAssignedAuthFailure = "The system assigned managed identity for this Function App does not have access to the resource configured in '{0}'.  See <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string UserAssignedAuthFailure = "The configured user assigned managed identity does not have access to the resource configured in '{0}'.  See <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string AuthFailureSummary = "Authentication failure. Credentials in connection string configured in app setting '{0}' are invalid or expired.";
        public const string AuthFailureBlobStorageDocs = "See <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-blob-trigger#connection-string' target='_blank'>relevant docs</a>.";
        public const string AuthFailureQueueStorageDocs = "See <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-storage-queue-trigger#connection-string' target='_blank'>relevant docs</a>.";
        public const string AuthFailureFileShareStorageDocs = "See <a href= 'https://docs.microsoft.com/azure/storage/common/storage-account-keys-manage' target='_blank'>relevant docs</a>.";
        public const string AuthFailureServiceBusDocs = "See <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger#connection-string' target='_blank'>relevant docs</a>.";
        public const string AuthFailureEventHubsDocs = "See <a href= 'https://docs.microsoft.com/azure/azure-functions/functions-bindings-event-hubs-trigger#connection-string' target='_blank'>relevant docs</a>.";
        public const string StorageAccessRestrictedDetails = "This may be due to firewall rules on the resource.  Please check if you have configured firewall rules or a private endpoint and that they correctly allow access from the Function App.  See <a href= 'https://docs.microsoft.com/en-us/azure/storage/common/storage-network-security?tabs=azure-portal' target='_blank'>Storage account network security</a> for additional details.";
        public const string ServiceBusAccessRestrictedDetails = "This may be due to firewall rules on the resource.  Please check if you have configured firewall rules or a private endpoint and that they correctly allow access from the Function App.  See <a href= 'https://docs.microsoft.com/en-us/azure/service-bus-messaging/network-security' target='_blank'>Service Bus network security</a> for additional details.";
        public const string EventHubAccessRestrictedDetails = "This may be due to firewall rules on the resource.  Please check if you have configured firewall rules or a private endpoint and that they correctly allow access from the Function App.  See <a href= 'https://docs.microsoft.com/en-us/azure/event-hubs/network-security' target='_blank'>Event Hubs network security</a> for additional details.";
        public const string DnsLookupFailed = "The service resource specified in the connection string was not found. Please check the value of the setting.";
        public const string FQNamespaceResourceNotFound = "The resource specified in the app setting '{0}" + FullyQualifiedNamespace + "' was not found. Please check the value of the setting.";
        public const string StorageAccountResourceNotFound = "The resource specified in the app setting '{0}' was not found. Please check the value of the setting.";
        public const string MalformedConnectionStringDetails = "The connection string configured in app setting '{0}' is invalid. Please check the value of the setting.";
        public const string EventHubEntityNotFoundSummary = "The configured Event Hub '{0}' was not found.";
        public const string EventHubEntityNotFoundDetails = "Refer to <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-event-hubs-trigger#attributes' target='_blank'>relevant docs</a> and check the value of the attribute 'EventHubName' in your code.";
        public const string ServiceBusEntityNotFoundSummary = "The configured Queue or Topic '{0}' was not found.";
        public const string ServiceBusEntityNotFoundDetails = "Refer to <a href='https://docs.microsoft.com/azure/azure-functions/functions-bindings-service-bus-trigger#attributes' target='_blank'>relevant docs</a> and check the value of the attribute 'QueueName' or 'TopicName' in your code.";
        public const string ManagedIdentityCredentialInvalidSummary = "The app setting '{0}" + Credential + "' is not valid.  To use identity-based connections, set its value to \"managedidentity\". See <a href='https://docs.microsoft.com/azure/azure-functions/functions-reference#common-properties-for-identity-based-connections' target='_blank'>relevant docs</a>. " + ManagedIdentityTutorial;
        public const string KeyVaultReferenceResolutionFailedSummary = "The Azure Key Vault reference configured in app setting '{0}' could not be resolved. See <a href='https://docs.microsoft.com/azure/app-service/app-service-key-vault-references' target='_blank'>relevant docs</a>.";
    }
}
