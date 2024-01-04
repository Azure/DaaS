// -----------------------------------------------------------------------
// <copyright file="AlertingStorageQueue.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using Azure.Storage.Queues;
using DaaS.Configuration;

namespace DaaS.Storage
{
    public class AlertingStorageQueue
    {
        private const int MessageExpirationInMinutes = 30;
        private QueueClient _queueClient;

        public AlertingStorageQueue()
        {
            string connectionString = Settings.Instance.StorageConnectionString;
            InitializeQueueClient(connectionString);
        }

        private void InitializeQueueClient(string connectionString)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    QueueServiceClient queueServiceClient = new QueueServiceClient(connectionString);
                    _queueClient = queueServiceClient.GetQueueClient(Settings.AlertQueueName);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Unhandled exception while initializing AlertingStorageQueue", ex);
            }
        }

        internal bool WriteMessageToAzureQueue(string message)
        {
            if (_queueClient == null)
            {
                return false;
            }

            _queueClient.CreateIfNotExists();
            _queueClient.SendMessage(message, null, TimeSpan.FromMinutes(MessageExpirationInMinutes));
            return true;
        }
    }
}
