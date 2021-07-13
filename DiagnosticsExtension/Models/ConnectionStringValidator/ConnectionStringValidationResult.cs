using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{

    public class ConnectionStringValidationResult
    {
        public enum ResultStatus
        {
            Success,
            AuthFailure,
            ConnectionFailure,
            EndpointNotFound,
            MsiFailure,
            UnknownError
        }

        public ResultStatus Status;
        public Exception Exception;
    }
}