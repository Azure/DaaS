// -----------------------------------------------------------------------
// <copyright file="E2EBackupTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Daas.Test;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Tests
{
    [Collection("E2EBackupTests")]
    public class E2EBackupTests
    {
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _client;
        private readonly HttpClient _websiteClient;
        private readonly string _webSiteInstances;

        public E2EBackupTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _client = Setup.GetHttpClient(configuration, "KUDU_ENDPOINT_BACKUP");
            _webSiteInstances = configuration["WEBSITE_INSTANCES"];
            _websiteClient = Setup.GetWebSiteHttpClient(configuration, "KUDU_ENDPOINT_BACKUP");
            _output = output;
        }

        [Fact]
        public async Task ProfilerV2X64MultipleInstances()
        {
            List<SiteInstance> siteInstances = JsonConvert.DeserializeObject<List<SiteInstance>>(_webSiteInstances);
            var requestedInstances = siteInstances.Select(x => x.machineName).ToList();
            await SessionTestHelpers.RunProfilerTest(_client, _websiteClient, _output, _webSiteInstances, requestedInstances: requestedInstances, isV2Session: true);
        }

        [Fact]
        public async Task MemoryDumpX64MultipleInstances()
        {
            List<SiteInstance> siteInstances = JsonConvert.DeserializeObject<List<SiteInstance>>(_webSiteInstances);
            var session = await SessionTestHelpers.SubmitNewSession("MemoryDump", _client, _websiteClient, _output, _webSiteInstances, requestedInstances: siteInstances.Select(x => x.machineName).ToList(), isV2Session: false);
            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);
        }

        [Fact]
        public async Task SubmitProfilerSessionV2()
        {
            var session = await SessionTestHelpers.SubmitNewSession("Profiler with Thread Stacks", _client, _websiteClient, _output, _webSiteInstances, requestedInstances: new List<string>(), isV2Session: true);
            await SessionTestHelpers.ValidateProfilerAsync(session, _client);
        }
    }
}
