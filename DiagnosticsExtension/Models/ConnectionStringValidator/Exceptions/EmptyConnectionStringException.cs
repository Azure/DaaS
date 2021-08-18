using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions
{
    public class EmptyConnectionStringException: Exception
    {
        public EmptyConnectionStringException() : base()
        { 
        }

        public EmptyConnectionStringException(string message) : base(message)
        { 
        }

        public EmptyConnectionStringException(string message, Exception innerException) : base(message, innerException)
        { 
        }
    }
}