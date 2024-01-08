// -----------------------------------------------------------------------
// <copyright file="SixtyFourBitE2eTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Daas.Tests;
using DaaS.Sessions;
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

            _client.Timeout = TimeSpan.FromMinutes(10);
        }

        [Fact]
        public async Task SubmitProfilerSessionX64()
        {
            await SessionTestHelpers.RunProfilerTest(_client, _websiteClient, _output);
        }

        [Fact]
        public async Task SubmitMemoryDumpSessionViaDiagLauncher()
        {
            var session = await SessionTestHelpers.SubmitDiagLauncherSessionAsync("MemoryDump", "CollectAndAnalyze", Status.Complete, _client, _websiteClient, _output);
            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);
            await SessionTestHelpers.EnsureDiagLauncherFinishedAsync(_client, _output);
        }

        [Fact]
        public async Task SubmitProfilerSessionViaDiagLauncher()
        {
            var submittedSession = await SessionTestHelpers.SubmitDiagLauncherSessionAsync("Profiler with Thread Stacks", "CollectKillAnalyze", Status.Active, _client, _websiteClient, _output);
            string sessionId = submittedSession.SessionId;
            var session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
            while (session.Status == Status.Active)
            {
                await Task.Delay(5000);
                session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
                Assert.NotNull(session);
            }

            await SessionTestHelpers.ValidateProfilerAsync(session, _client);
            await SessionTestHelpers.EnsureDiagLauncherFinishedAsync(_client, _output);
        }

        [Fact]
        public async Task MemoryDumpInvokedViaAutoHealingDiagLauncher()
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

            await Task.Delay(30000);

            var session = await SessionTestHelpers.GetActiveSessionAsync(_client, _websiteClient, _output);
            var sessionId = session.SessionId;
            while (session.Status == Status.Active)
            {
                await Task.Delay(5000);
                session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
                Assert.NotNull(session);
            }

            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);

            await SessionTestHelpers.EnsureDiagLauncherFinishedAsync(_client, _output);
        }
    }
}
