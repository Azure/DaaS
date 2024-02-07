// -----------------------------------------------------------------------
// <copyright file="AzureStorageServiceSasUriTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Daas.Test;
using DaaS.Storage;
using Xunit;

namespace Daas.Test
{
    public class AzureStorageServiceSasUriTests : AzureStorageServiceTestsBase
    {
        public AzureStorageServiceSasUriTests() :
            base(new AzureStorageService(string.Empty, Setup.GetConfiguration()["WEBSITE_DAAS_STORAGE_SASURI"]), DateTime.UtcNow.Ticks.ToString())
        {
            TestHelpers.SetupTestEnvironment();
        }
    }
}
