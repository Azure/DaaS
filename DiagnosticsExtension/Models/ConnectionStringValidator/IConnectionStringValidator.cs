//-----------------------------------------------------------------------
// <copyright file="IConnectionStringValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    interface IConnectionStringValidator
    {
        // verify provided string is a valid connection string that can be tested by the validator
        Task<bool> IsValidAsync(string connStr);

        Task<ConnectionStringValidationResult> ValidateAsync(string connStr, string clientId = null);  // clientId used for Used Assigned Managed Identity

        string ProviderName { get; }

        ConnectionStringType Type { get; }


    }
}
