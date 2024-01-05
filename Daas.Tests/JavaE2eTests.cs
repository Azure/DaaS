// -----------------------------------------------------------------------
// <copyright file="JavaE2eTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Daas.Tests;
using DaaS.Sessions;
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

        public JavaE2eTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _clientJava = Setup.GetHttpClient(configuration, "KUDU_ENDPOINT_JAVA");
            _websiteClientJava = Setup.GetWebSiteHttpClient(configuration, "KUDU_ENDPOINT_JAVA");
            _output = output;
        }

        [Fact]
        public async Task SubmitJavaThreadDump()
        {
            await SubmitJavaTool("JAVA Thread Dump", "_jstack.log", "_jstack_");
        }

        [Fact]
        public async Task SubmitJavaFlighRecorderTrace()
        {
            await SubmitJavaTool("JAVA Flight Recorder", "_jcmd.jfr", "_jcmd");
        }

        [Fact]
        public async Task SubmitJavaMemoryDump()
        {
            await SubmitJavaTool("JAVA Memory Dump", "_MemoryDump.bin", "_MemoryDump");
        }

        private async Task SubmitJavaTool(string toolName, string logFileContains, string reportFileContains, long minDumpSize = 1024, long maxDumpSize = 1024 * 1024 * 1024)
        {
            var session = await SessionTestHelpers.SubmitNewSession(toolName, _clientJava, _websiteClientJava, _output);
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(logFileContains, log.Name);
            _output.WriteLine("Log file name is " + log.Name);

            Assert.InRange<long>(log.Size, minDumpSize, maxDumpSize);

            Report htmlReport = log.Reports.FirstOrDefault(r => r.Name.EndsWith(".html") && r.Name.ToLowerInvariant().Contains(reportFileContains.ToLowerInvariant()));
            Assert.NotNull(htmlReport);
            _output.WriteLine("Report file name is " + htmlReport.Name);
        }

    }
}
