// -----------------------------------------------------------------------
// <copyright file="ActiveInstanceEntity.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using Azure;
using Azure.Data.Tables;

namespace DaaS.Sessions
{
    public class ActiveInstanceEntity : ITableEntity
    {
        #region ITableEntity members

        /// <summary>
        /// Partition Key is the DefaultHostName for the app
        /// </summary>
        public string PartitionKey { get; set; }

        /// <summary>
        /// RowKey is the {SessionId}_{InstanceName}. This is done to ensure that
        /// PartitionKey + RowKey becomes a primary key for that table
        /// </summary>
        public string RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }
        #endregion

        public string Status { get; set; }
        public string InstanceName { get; set; }
        public string SessionId { get; set; }
        public string LogFilesJson { get; set; }
        public string CollectorErrorsJson { get; set; }
        public string AnalyzerErrorsJson { get; set; }
    }
}
