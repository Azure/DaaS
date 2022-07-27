// -----------------------------------------------------------------------
// <copyright file="ConnectionStringType.cs" company="Microsoft Corporation">
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
    public enum ConnectionStringType
    {
        SqlServer,
        MySql,
        KeyVault,
        Http,
        RedisCache,
        ServiceBus,
        EventHubs,
        StorageAccount,
        BlobStorageAccount,
        QueueStorageAccount,
        FileShareStorageAccount,
    }
}
