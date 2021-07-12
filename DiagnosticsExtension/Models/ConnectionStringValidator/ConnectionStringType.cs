using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public enum ConnectionStringType
    {
        SqlServer,
        RedisCache,
        StorageAccount,
        Http
    }
}