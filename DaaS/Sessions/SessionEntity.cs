// -----------------------------------------------------------------------
// <copyright file="SessionEntity.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.ComponentModel;
using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;

namespace DaaS.Sessions
{
    public class SessionEntity : ITableEntity
    {
        /// <summary>
        /// PartionKey is DefaultHostName
        /// </summary>
        public string PartitionKey { get; set; }

        /// <summary>
        /// RowKey is SessionId
        /// </summary>
        public string RowKey { get; set; }

        public ETag ETag { get; set; }
        public string Status { get; set; }
        public string Description { get; set; }
        public string BlobStorageHostName { get; set; }
        public DateTime StartTime { get; set; }
        public string Tool { get; set; }
        public string Mode { get; set; }
        public string ToolParams { get; set; }
        public string InstancesJson { get; set; }
        public string ActiveInstancesJson { get; set; }
        public DateTime? EndTime { get; set; }
        DateTimeOffset? ITableEntity.Timestamp { get ; set ;}

        public SessionEntity(Session session, string defaultHostName)
        {
            if (string.IsNullOrWhiteSpace(session.SessionId))
            {
                throw new NullReferenceException("SessionId is empty");
            }

            Tool = session.Tool.ToString();
            ToolParams = session.ToolParams;
            Mode = session.Mode.ToString();
            InstancesJson = JsonConvert.SerializeObject(session.Instances);
            PartitionKey = defaultHostName;
            RowKey = session.SessionId;
            Status = Sessions.Status.Active.ToString();
            Description = session.Description;
            BlobStorageHostName = session.BlobStorageHostName;
            StartTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        }

        public SessionEntity()
        {
        }
    }
}
