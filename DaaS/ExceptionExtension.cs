// -----------------------------------------------------------------------
// <copyright file="ExceptionExtension.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS
{
    public static class ExceptionExtension
    {
        public static string ToLogString(this Exception exception)
        {
            if (exception == null)
            {
                return string.Empty;
            }
            else
            {
                string exceptionLog =  $"{exception.GetType()}:{exception.Message} {Environment.NewLine} {exception.StackTrace}";
                if (exception.InnerException != null)
                {
                    exceptionLog = $"{exceptionLog}{Environment.NewLine}InnerException={exception.InnerException.GetType()}:{exception.InnerException.Message} {Environment.NewLine} {exception.InnerException.StackTrace} ";
                }

                return exceptionLog;
            }
        }
    }
}
