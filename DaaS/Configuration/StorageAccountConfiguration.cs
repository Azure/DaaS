// -----------------------------------------------------------------------
// <copyright file="StorageAccountConfiguration.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS.Configuration
{
    public class StorageAccountConfiguration
    {
        public string SasUri { get; set; }
        public DateTimeOffset DateAdded { get; set; }
    }
}
