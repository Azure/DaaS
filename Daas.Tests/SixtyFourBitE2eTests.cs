// -----------------------------------------------------------------------
// <copyright file="SixtyFourBitE2eTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Threading.Tasks;
using Daas.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Test
{
    [Collection("SixtyFourBitE2eTests")]
    public class SixtyFourBitE2eTests
    {
        private readonly HttpClient _client;
        private readonly HttpClient _websiteClient;
        private readonly ITestOutputHelper _output;

        public SixtyFourBitE2eTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _client = Setup.GetHttpClient(configuration, "KUDU_ENDPOINT_X64");
            _websiteClient = Setup.GetWebSiteHttpClient(configuration, "KUDU_ENDPOINT_X64");
            _output = output;
        }

        [Fact]
        public async Task SubmitProfilerSessionX64()
        {
            await SessionTestHelpers.RunProfilerTest(_client, _websiteClient, _output);
        }
    }
}
