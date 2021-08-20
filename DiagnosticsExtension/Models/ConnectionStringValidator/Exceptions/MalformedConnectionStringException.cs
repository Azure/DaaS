using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions
{
    public class MalformedConnectionStringException: Exception
    {
        public MalformedConnectionStringException() : base()
        { 
        }

        public MalformedConnectionStringException(string message) : base(message)
        { 
        }

        public MalformedConnectionStringException(string message, Exception innerException) : base(message, innerException)
        { 
        }
    }
}