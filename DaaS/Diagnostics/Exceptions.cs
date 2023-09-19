// -----------------------------------------------------------------------
// <copyright file="Exceptions.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS.Diagnostics
{
    public class DiagnosticSessionAbortedException : Exception
    {
        public DiagnosticSessionAbortedException(string message, Exception innerException) : base(GetExceptionMessage(message), innerException) {}
        public DiagnosticSessionAbortedException(string message) : base(GetExceptionMessage(message)) { }

        private static string GetExceptionMessage(string message = "")
        {
            return $"Failed to submit session - {message}";
        }
    }

    class DiagnosticToolErrorException : Exception
    {
        public DiagnosticToolErrorException(string message) : base(GetExceptionMessage(message)) { }
        public DiagnosticToolErrorException(System.Exception inner) : base(GetExceptionMessage(), inner) { }

        private static string GetExceptionMessage(string message = "")
        {
            return string.Format("Tool exited with an error. {0}", message);
        }
    }

    class DiagnosticToolHasNoOutputException : System.Exception
    {
        public DiagnosticToolHasNoOutputException(string toolName, string additionalText = "") : base(GetExceptionMessage(toolName, additionalText)) { }

        private static string GetExceptionMessage(string toolName, string additionalText = "")
        {
            string message = $"{toolName} did not generate any logs or records. ";
            if (!string.IsNullOrWhiteSpace(additionalText))
            {
                message += additionalText;
            }
            return message;
        }
    }
}
