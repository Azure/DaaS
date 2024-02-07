// -----------------------------------------------------------------------
// <copyright file="AzureStorageServiceConnectionStringTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using DaaS.Storage;

namespace Daas.Test
{
    public class AzureStorageServiceConnectionStringTests : AzureStorageServiceTestsBase
    {
        public AzureStorageServiceConnectionStringTests():
            base(new AzureStorageService(Setup.GetConfiguration()["WEBSITE_DAAS_STORAGE_CONNECTIONSTRING"], string.Empty), DateTime.UtcNow.Ticks.ToString() + "_CS")
        {
        }

    }
}
