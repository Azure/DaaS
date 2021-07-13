using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public enum ConnectionStringType
    {
        // Jeff
        SqlServer,
        MySql,
        KeyVault,
        Http,
        RedisCache,

        // Sid
        StorageAccount,
        ServiceBus,
        EventHubs,
        CosmosDB
    }
}