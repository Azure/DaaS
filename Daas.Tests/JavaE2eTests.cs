// -----------------------------------------------------------------------
// <copyright file="JavaE2eTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using DaaS.Sessions;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Test
{
    [Collection("JavaE2eTests")]
    public class JavaE2eTests
    {
        private readonly HttpClient _clientJava;
        private readonly HttpClient _websiteClientJava;
        private readonly ITestOutputHelper _output;
        private readonly string _webSiteInstances;

        public JavaE2eTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _clientJava = Setup.GetHttpClient(configuration, "KUDU_ENDPOINT_JAVA");
            _websiteClientJava = Setup.GetWebSiteHttpClient(configuration, "KUDU_ENDPOINT_JAVA");
            _webSiteInstances = configuration["WEBSITE_INSTANCES"];
            _output = output;
        }

        [Fact]
        public async Task JavaThreadDump()
        {
            await SubmitJavaTool("JAVA Thread Dump", "_jstack.log", "_jstack_");
        }

        [Fact]
        public async Task JavaThreadDumpV2()
        {
            await SubmitJavaTool("JAVA Thread Dump", "_jstack.log", "_jstack_", 1024, 1073741824, null, isV2Session: true);
        }

        [Fact]
        public async Task JavaThreadDumpV2MultipleInstances()
        {
            List<SiteInstance> siteInstances = JsonConvert.DeserializeObject<List<SiteInstance>>(_webSiteInstances);
            var requestedInstances = siteInstances.Select(x => x.machineName).ToList();
            await SubmitJavaTool("JAVA Thread Dump", "_jstack.log", "_jstack_", 1024, 1073741824, requestedInstances, isV2Session: true);
        }

        [Fact]
        public async Task JavaFlighRecorder()
        {
            await SubmitJavaTool("JAVA Flight Recorder", "_jcmd.jfr", "_jcmd");
        }

        [Fact]
        public async Task JavaMemoryDump()
        {
            await SubmitJavaTool("JAVA Memory Dump", "_MemoryDump.bin", "_MemoryDump");
        }

        private async Task SubmitJavaTool(string toolName, string logFileContains, string reportFileContains, long minDumpSize = 1024, long maxDumpSize = 1024 * 1024 * 1024, List <string> requestedInstances = null, bool isV2Session = false)
        {
            if (requestedInstances == null)
            {
                requestedInstances = new List<string>();
            }

            var session = await SessionTestHelpers.SubmitNewSession(toolName, _clientJava, _websiteClientJava, _output, _webSiteInstances, requestedInstances: requestedInstances, isV2Session: isV2Session);
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(logFileContains, log.Name);
            _output.WriteLine($"[{DateTime.UtcNow}] Log file name is " + log.Name);

            Assert.InRange<long>(log.Size, minDumpSize, maxDumpSize);

            Report htmlReport = log.Reports.FirstOrDefault(r => r.Name.EndsWith(".html") && r.Name.ToLowerInvariant().Contains(reportFileContains.ToLowerInvariant()));
            Assert.NotNull(htmlReport);
            _output.WriteLine($"[{DateTime.UtcNow}] Report file name is " + htmlReport.Name);
        }

    }
}
