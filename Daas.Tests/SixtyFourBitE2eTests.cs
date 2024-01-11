// -----------------------------------------------------------------------
// <copyright file="SixtyFourBitE2eTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Daas.Tests;
using DaaS.Sessions;
using Newtonsoft.Json;
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
        private readonly string _webSiteInstances;

        public SixtyFourBitE2eTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _client = Setup.GetHttpClient(configuration, "KUDU_ENDPOINT_X64");
            _websiteClient = Setup.GetWebSiteHttpClient(configuration, "KUDU_ENDPOINT_X64");
            _output = output;
            _webSiteInstances = configuration["WEBSITE_INSTANCES"];
            _client.Timeout = TimeSpan.FromMinutes(10);
        }

        [Fact]
        public async Task ProfilerX64()
        {
            await SessionTestHelpers.RunProfilerTest(_client, _websiteClient, _output, _webSiteInstances, requestedInstances: new List<string>());
        }

        [Fact]
        public async Task ProfilerX64MultipleInstances()
        {
            List <SiteInstance> siteInstances = JsonConvert.DeserializeObject<List<SiteInstance>>(_webSiteInstances);
            var requestedInstances = siteInstances.Select(x => x.machineName).ToList();
            await SessionTestHelpers.RunProfilerTest(_client, _websiteClient, _output, _webSiteInstances, requestedInstances: requestedInstances);
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
        public async Task MemoryDumpViaAutoHealDiagLauncher()
        {
            var warmupMessage = await SessionTestHelpers.EnsureSiteWarmedUpAsync(_websiteClient);
            _output.WriteLine("Warmup message is: " + warmupMessage);

            //
            // For this case, the DevOps pipeline will create an app 'KUDU_ENDPOINT_X64'
            // that has these autoHeal configuration
            //

            // $autohealRulesDotNet = @
            // {
            //  triggers =@{ requests =@{ count = 50; timeInterval = "00:02:00"} };
            //  actions =@{ actionType = "CustomAction"; customAction =@{ exe = "`" % WEBSITE_DAAS_DIAG_LAUNCHER %`""; parameters = "-m CollectAndAnalyze -t MemoryDump"}};
            // }

            await SessionTestHelpers.StressTestWebAppAsync(requestCount: 55, _websiteClient, _output);
            _output.WriteLine("Stess test completed at " + DateTime.UtcNow.ToString("O"));

            await Task.Delay(15000);

            _output.WriteLine("Getting Active session at " + DateTime.UtcNow.ToString("O"));
            var session = await SessionTestHelpers.GetActiveSessionAsync(_client, _websiteClient, _output);
            Assert.NotNull(session);

            var sessionId = session.SessionId;
            while (session.Status == Status.Active)
            {
                await Task.Delay(30000);
                session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
                Assert.NotNull(session);
            }

            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);

            await SessionTestHelpers.EnsureDiagLauncherFinishedAsync(_client, _output);
        }

        [Fact]
        public async Task SubmitProfilerSessionV2()
        {
            var session = await SessionTestHelpers.SubmitNewSession("Profiler with Thread Stacks", _client, _websiteClient, _output, _webSiteInstances, requestedInstances: new List<string>(), isV2Session: true);
            await SessionTestHelpers.ValidateProfilerAsync(session, _client);
        }
    }
}
