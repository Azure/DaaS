﻿// -----------------------------------------------------------------------
// <copyright file="EndToEndTests.cs" company="Microsoft Corporation">
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
using DiagnosticsExtension.Controllers;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Test
{
    [Collection("E2eTests")]
    public class E2eTests
    {
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _client;
        private readonly HttpClient _websiteClient;
        private readonly string _webSiteInstances;

        public E2eTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _client = Setup.GetHttpClient(configuration, "KUDU_ENDPOINT");
            _webSiteInstances = configuration["WEBSITE_INSTANCES"];
            _websiteClient = Setup.GetWebSiteHttpClient(configuration, "KUDU_ENDPOINT");
            _output = output;
        }

        [Fact]
        public async Task GetAllSessions()
        {
            var resp = await _client.PostAsync("daas/sessions/list", null);
            var respStatusCode = resp.StatusCode.ToString();
            Assert.True(resp.IsSuccessStatusCode, $"GetAllSessions returned {respStatusCode}");
        }

        [Fact]
        public async Task CheckDaasVersions()
        {
            var resp = await _client.GetAsync("daas/api/v2/daasversion");
            var respStatusCode = resp.StatusCode.ToString();
            Assert.True(resp.IsSuccessStatusCode, $"daasversion returned {respStatusCode}");

            var daasVersionString = await resp.Content.ReadAsStringAsync();
            _output.WriteLine($"DaasVersion = {daasVersionString}");

            var daasVersion = JsonConvert.DeserializeObject<DaasConfig>(daasVersionString);

            Assert.Equal(daasVersion.Version, daasVersion.DaasConsoleVersion);
            Assert.Equal(daasVersion.Version, daasVersion.DaasRunnerVersion);
        }

        [Fact]
        public async Task CheckIfKnownFilesExist()
        {
            var knownFiles = new string[]
            {
                "DiagnosticAnalysis/ResultsViewer.html",
                "bin/Daas.dll",
                "bin/DaaSConsole.exe",
                "bin/DaaSConsole.exe.config",
                "bin/DaaSRunner.exe.config",
                "bin/configuration/DiagnosticSettings.json",
                "bin/DiagnosticTools/DiagnosticAnalysisLauncher.exe",
                "bin/DiagnosticTools/DumpAnalyzer.ps1",
                "bin/DiagnosticTools/MemoryDumpCollector.exe",
                "bin/DiagnosticTools/JavaValidator.ps1",
                "bin/DiagnosticTools/javatools/analyzeJavaTool.ps1",
                "bin/DiagnosticTools/javatools/collectjCmd.ps1",
                "bin/DiagnosticTools/javatools/collectJmap.ps1",
                "bin/DiagnosticTools/javatools/collectJStack.ps1",
                "bin/DiagnosticTools/javatools/daas.dll",
                "bin/DiagnosticTools/javatools/jcmdAnalysis.html",
                "bin/DiagnosticTools/javatools/jmapAnalysis.html",
                "bin/DiagnosticTools/javatools/jStackParser.exe",
                "bin/DiagnosticTools/clrprofiler/ClrProfilingCollector.exe",
                "bin/DiagnosticTools/clrprofiler/ClrProfilingAnalyzer.exe",
                "bin/DiagnosticTools/clrprofiler/DiagnosticsHub.Packaging.Interop.dll",
                "bin/DiagnosticTools/clrprofiler/OSExtensions.dll",
                "bin/DiagnosticTools/clrprofiler/daas.dll",
                "bin/DiagnosticTools/clrprofiler/TraceReloggerLib.dll",
                "bin/DiagnosticTools/clrprofiler/x86/DiagnosticsHub.Packaging.dll",
                "bin/DiagnosticTools/clrprofiler/x86/KernelTraceControl.dll",
                "bin/DiagnosticTools/clrprofiler/x86/msdia140.dll",
                "bin/DiagnosticTools/clrprofiler/amd64/KernelTraceControl.dll",
                "bin/DiagnosticTools/clrprofiler/amd64/msdia140.dll",
                "bin/DiagnosticTools/clrprofiler/x64/DiagnosticsHub.Packaging.dll",
                "bin/DiagnosticTools/clrprofiler/x64/DiagnosticsHub.Packaging.dll",
                "bin/DiagnosticTools/clrprofiler/stacktracer/stacktracer32.exe",
                "bin/DiagnosticTools/clrprofiler/stacktracer/StackTracer64.exe",
                "bin/DiagnosticTools/clrprofiler/stacktracer/StackTracerCore.dll",
                "bin/DiagnosticTools/clrprofiler/staticcontent/index.html",
                "bin/DiagnosticTools/clrprofiler/staticcontent/solutionmap/AspNetCoreModule.html",
                "bin/DiagnosticTools/clrprofiler/staticcontent/js/controller.js",
                "bin/DiagnosticTools/clrprofiler/staticcontent/js/default.js",
                "bin/DiagnosticTools/clrprofiler/staticcontent/js/Chart.PieceLabel.js",
            };

            foreach (var file in knownFiles)
            {
                _output.WriteLine($"Checking for file {file}");
                //var resp = await _client.GetAsync($"api/vfs/SystemDrive/Program%20Files%20(x86)/SiteExtensions/DaaS/4.2.24207.2/{file}");
                var resp = await _client.GetAsync($"api/vfs/siteextensions/daas/{file}");
                Assert.True(resp.IsSuccessStatusCode, $"File - api/vfs/siteextensions/daas/{file}");
            }
        }

        [Fact]
        public async Task MemoryDump()
        {
            var session = await SessionTestHelpers.SubmitNewSession("MemoryDump", _client, _websiteClient, _output, _webSiteInstances, requestedInstances: new List<string>());
            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);
        }

        [Fact]
        public async Task MemoryDumpV2()
        {
            var session = await SessionTestHelpers.SubmitNewSession("MemoryDump", _client, _websiteClient, _output, _webSiteInstances, requestedInstances: new List<string>(),  isV2Session: true);
            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);
        }


        [Fact]
        public async Task ProfilerSession()
        {
            var session = await SessionTestHelpers.RunProfilerTest(_client, _websiteClient, _output, _webSiteInstances, requestedInstances: new List<string>());
            await SessionTestHelpers.ValidateProfilerAsync(session, _client);
        }

        [Fact]
        public async Task ListDiagnosersFromDaasConsole()
        {

            var daasConsoleResponseMessage = await _client.PostAsJsonAsync("api/command", new { command = "DaasConsole.exe -ListDiagnosers", dir = "data\\DaaS\\bin" });
            daasConsoleResponseMessage.EnsureSuccessStatusCode();

            string daasConsoleResponse = await daasConsoleResponseMessage.Content.ReadAsStringAsync();
            var apiCommandResponse = JsonConvert.DeserializeObject<ApiCommandResponse>(daasConsoleResponse);
            _output.WriteLine("ListDiagnosers response is " + apiCommandResponse.Output);
        }

        

        [Fact]
        public async Task MockSessionFromDaasConsole()
        {
            var daasConsoleResponseMessage = await _client.PostAsJsonAsync("api/command", new { command = "DaasConsole.exe -Troubleshoot Mock", dir = "data\\DaaS\\bin" });
            daasConsoleResponseMessage.EnsureSuccessStatusCode();

            string daasConsoleResponse = await daasConsoleResponseMessage.Content.ReadAsStringAsync();
            var apiCommandResponse = JsonConvert.DeserializeObject<ApiCommandResponse>(daasConsoleResponse);
            _output.WriteLine("'DaasConsole.exe -Troubleshoot Mock' response is " + apiCommandResponse.Output);

            string sessionId = string.Empty;
            var daasConsoleOutput = apiCommandResponse.Output.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            foreach(var line in daasConsoleOutput)
            {
                if (line.StartsWith("Session submitted for "))
                {
                    sessionId = line.Split(' ').Last();
                    break;
                }
            }

            _output.WriteLine("SessionId is " + sessionId);

            Assert.True(!string.IsNullOrWhiteSpace(sessionId), "SubmitMockSessionFromDaasConsole should not return empty SessionId");

            var session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
            Assert.NotNull(session);

            while (session.Status == Status.Active)
            {
                await Task.Delay(5000);
                session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
                Assert.NotNull(session);
            }

            SessionTestHelpers.CheckSessionAsserts(session);
        }

        [Fact]
        public async Task ProfilerViaAutoHealDiagLauncher()
        {
            var warmupMessage = await SessionTestHelpers.EnsureSiteWarmedUpAsync(_websiteClient);
            _output.WriteLine("Warmup message is: " + warmupMessage);

            //
            // For this case, the DevOps pipeline will create an app 'KUDU_ENDPOINT'
            // that has autoHeal configuration to start DiagLauncher
            //

            // $autohealRulesDotNet = @
            // {
            //  triggers =@{ requests =@{ count = 30; timeInterval = "00:02:00"} };
            //  actions=@{actionType="CustomAction";customAction=@{exe="`"%WEBSITE_DAAS_EXTENSIONPATH%\DiagLauncher\DiagLauncher.exe`"";parameters="-m CollectAndAnalyze -t Profiler"}};
            // }

            //
            // Set to 40 requests as tests are running on 2 instances now
            //

            await SessionTestHelpers.StressTestWebAppAsync(requestCount: 40, _websiteClient, _output);

            await Task.Delay(20000);

            var session = await SessionTestHelpers.GetActiveSessionAsync(_client, _websiteClient, _output);
            Assert.NotNull(session);

            var sessionId = session.SessionId;
            while (session.Status == Status.Active)
            {
                await Task.Delay(30000);
                session = await SessionTestHelpers.GetSessionInformationAsync(sessionId, _client, _output);
            }

            await SessionTestHelpers.ValidateProfilerAsync(session, _client);
            await SessionTestHelpers.EnsureDiagLauncherFinishedAsync(_client,_output);
        }

        [Fact]
        public async Task MemoryDumpV2MultipleInstances()
        {
            List<SiteInstance> siteInstances = JsonConvert.DeserializeObject<List<SiteInstance>>(_webSiteInstances);
            var requestedInstances = siteInstances.Select(x => x.machineName).ToList();

            var session = await SessionTestHelpers.SubmitNewSession("MemoryDump", _client, _websiteClient, _output, _webSiteInstances, requestedInstances: requestedInstances, isV2Session: true);
            await SessionTestHelpers.ValidateMemoryDumpAsync(session, _client);
        }
    }

    class ApiCommandResponse
    {
        public string Output { get; set; }
        public string Error { get; set; }
        public int ExitCode { get; set; }
    }
}
