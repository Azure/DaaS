// -----------------------------------------------------------------------
// <copyright file="EmptyConnectionStringException.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

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