// -----------------------------------------------------------------------
// <copyright file="Exceptions.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DaaS.Diagnostics
{
    class DiagnosticToolErrorException : System.Exception
    {
        //public DiagnosticToolErrorException() : base() { }
        public DiagnosticToolErrorException(string message) : base(GetExceptionMessage(message)) { }
        public DiagnosticToolErrorException(System.Exception inner) : base(GetExceptionMessage(), inner) { }

        private static string GetExceptionMessage(string message = "")
        {
            return string.Format("Tool exited with an error. {0}", message);
        }
    }
    class DiagnosticToolHasNoOutputException : System.Exception
    {
        //public DiagnosticToolHasNoOutputException() : base() { }
        public DiagnosticToolHasNoOutputException(string toolName, string additionalText = "") : base(GetExceptionMessage(toolName, additionalText)) { }
        //public DiagnosticToolHasNoOutputException(string toolName, System.Exception inner) : base(GetExceptionMessage(toolName), inner) { }

        private static string GetExceptionMessage(string toolName, string additionalText = "")
        {
            string message = $"{toolName} did not generate any logs or records. ";
            if (!string.IsNullOrWhiteSpace(additionalText))
            {
                message = message + additionalText;
            }
            return message;
        }
    }
}
