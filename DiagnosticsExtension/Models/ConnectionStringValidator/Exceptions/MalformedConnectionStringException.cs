﻿// -----------------------------------------------------------------------
// <copyright file="MalformedConnectionStringException.cs" company="Microsoft Corporation">
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