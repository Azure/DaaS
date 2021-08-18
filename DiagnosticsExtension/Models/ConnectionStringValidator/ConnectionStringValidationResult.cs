using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{

    public class ConnectionStringValidationResult
    {
        public ResultStatus? Status;
        public string StatusText => Status?.ToString();
        public Exception Exception;
        public object Payload;
        public string Type => type.ToString();

        private ConnectionStringType type;

        public ConnectionStringValidationResult(ConnectionStringType type)
        {
            this.type = type;
        }
        public enum ResultStatus
        {
            Success,
            AuthFailure,
            ContentNotFound,
            Forbidden,
            UnknownResponse,
            EndpointNotReachable,
            ConnectionFailure,
            DnsLookupFailed,
            MsiFailure,
            EmptyConnectionString,
            MalformedConnectionString,
            UnknownError
        }

        
    }
}