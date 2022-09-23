// -----------------------------------------------------------------------
// <copyright file="EndToEndTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DaaS.Sessions;
using DiagnosticsExtension.Controllers;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Test
{
    public class E2eTests
    {
        private readonly ITestOutputHelper _output;
        private readonly HttpClient _client;

        public E2eTests(ITestOutputHelper output)
        {
            var configuration = Setup.GetConfiguration();
            _client = Setup.GetHttpClient(configuration);
            _output = output;
        }

        [Fact]
        public async Task GetAllSessions_ShouldReturnValidHttpResponse()
        {
            var resp = await _client.GetAsync("daas/sessions");
            Assert.True(resp.IsSuccessStatusCode);
        }

        [Fact]
        public async Task CheckDaasVersions_ShouldReturnSameVersions()
        {
            var resp = await _client.GetAsync("daas/api/v2/daasversion");
            Assert.True(resp.IsSuccessStatusCode);

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
                //var resp = await _client.GetAsync($"api/vfs/SystemDrive/Program%20Files%20(x86)/SiteExtensions/DaaS/3.1.22826.01/{file}");
                var resp = await _client.GetAsync($"api/vfs/siteextensions/daas/{file}");
                Assert.True(resp.IsSuccessStatusCode);
            }
        }

        [Fact]
        public async Task SubmitMemoryDumpSession()
        {
            var session = await SubmitNewSession("MemoryDump");
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(".dmp", log.Name);
            Assert.True(!string.IsNullOrWhiteSpace(session.BlobStorageHostName));

            //
            // Just ensure that size returned is within 50MB - 5GB
            //

            long minDumpSize = 52428800; // 50 MB
            long maxDumpSize = 5368709120; // 5GB
            Assert.InRange<long>(log.Size, minDumpSize, maxDumpSize);

            // simple sanity check that verifies the html report contains a reference to the dmp file (for the "Open in VS" scenario")
            Report htmlReport = log.Reports.FirstOrDefault(r => r.Name.EndsWith(".html"));
            Assert.NotNull(htmlReport);

            var htmlReportResponse = await _client.GetAsync("api/vfs/" + htmlReport.PartialPath);
            Assert.True(htmlReportResponse.IsSuccessStatusCode);

            var htmlReportContent = await htmlReportResponse.Content.ReadAsStringAsync();

            var dmpBlobUri = log.RelativePath.Split('?')[0];// remove the SAS token URL params
            Assert.True(htmlReportContent.Contains(dmpBlobUri), "The HTML report needs to contain a reference to the Azure Storage blob containing the dump.");

            var storageAccountName = session.BlobStorageHostName.Split('.')[0];
            var storageResourceIdRegex = new Regex($"/subscriptions/[a-z0-9\\-]+/resourceGroups/[\\w0-9\\-_\\(\\)\\.]+/providers/Microsoft\\.Storage/storageAccounts/{storageAccountName}");
            Assert.True(storageResourceIdRegex.IsMatch(htmlReportContent), "The HTML report needs to contain a reference to the Azure Storage resource id containing the dump.");
        }

        [Fact]
        public async Task SubmitProfilerSession()
        {
            var session = await SubmitNewSession("Profiler with Thread Stacks");
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(".zip", log.Name);

            //
            // Just ensure that size returned is within 1kb - 100MB
            //

            long minFileSize = 1024; // 1kb
            long maxFileSize = 100 * 1024 * 1024; // 100MB
            Assert.InRange(log.Size, minFileSize, maxFileSize);
        }

        [Fact]
        public async Task InvokeListDiagnosersFromDaasConsole()
        {

            var daasConsoleResponseMessage = await _client.PostAsJsonAsync("api/command", new { command = "DaasConsole.exe -ListDiagnosers", dir = "data\\DaaS\\bin" });
            daasConsoleResponseMessage.EnsureSuccessStatusCode();

            string daasConsoleResponse = await daasConsoleResponseMessage.Content.ReadAsStringAsync();
            var apiCommandResponse = JsonConvert.DeserializeObject<ApiCommandResponse>(daasConsoleResponse);
            _output.WriteLine("ListDiagnosers response is " + apiCommandResponse.Output);
        }

        [Fact]
        public async Task SubmitMockSessionFromDaasConsole()
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

            Assert.True(!string.IsNullOrWhiteSpace(sessionId));

            var session = await GetSessionInformation(sessionId);
            while (session.Status == Status.Active)
            {
                await Task.Delay(5000);
                session = await GetSessionInformation(sessionId);
            }

            CheckSessionAsserts(session);
        }

        private async Task<Session> SubmitNewSession(string diagnosticTool)
        {
            var machineName = await GetMachineName();
            var newSession = new Session()
            {
                Mode = Mode.CollectAndAnalyze,
                Tool = diagnosticTool,
                Instances = new List<string> { machineName }
            };

            var response = await _client.PostAsJsonAsync("daas/sessions", newSession);
            Assert.NotNull(response);

            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);

            string sessionIdResponse = await response.Content.ReadAsStringAsync();
            Assert.NotNull(sessionIdResponse);

            _output.WriteLine("SessionId Response is " + sessionIdResponse);

            string sessionId = JsonConvert.DeserializeObject<string>(sessionIdResponse);

            var session = await GetSessionInformation(sessionId);
            while (session.Status == Status.Active)
            {
                await Task.Delay(15000);
                session = await GetSessionInformation(sessionId);
            }

            CheckSessionAsserts(session);

            return session;
        }

        private static void CheckSessionAsserts(Session session)
        {
            Assert.Equal(Status.Complete, session.Status);
            Assert.False(session.EndTime == DateTime.MinValue || session.StartTime == DateTime.MinValue);

            Assert.True(!string.IsNullOrWhiteSpace(session.Description));
            Assert.True(!string.IsNullOrWhiteSpace(session.DefaultScmHostName));

            Assert.NotNull(session.ActiveInstances);
            Assert.NotEmpty(session.ActiveInstances);

            Assert.NotNull(session.ActiveInstances.FirstOrDefault().Logs);
            Assert.NotNull(session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault().Reports);
            Assert.NotNull(session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault().Reports.FirstOrDefault());

            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            var report = log.Reports.FirstOrDefault();

            Assert.NotNull(report.Name);
            Assert.NotNull(report.RelativePath);

            Assert.StartsWith("https://", report.RelativePath, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("https://", log.RelativePath, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<Session> GetSessionInformation(string sessionId)
        {
            var sessionResponse = await _client.GetAsync($"daas/sessions/{sessionId}");
            sessionResponse.EnsureSuccessStatusCode();

            string sessionString = await sessionResponse.Content.ReadAsStringAsync();
            var session = JsonConvert.DeserializeObject<Session>(sessionString);
            return session;
        }

        private async Task<string> GetMachineName()
        {
            var machineResponseMessage = await _client.PostAsJsonAsync("api/command", new { command = "hostname", dir = "site" });
            machineResponseMessage.EnsureSuccessStatusCode();

            string machineNameResponse = await machineResponseMessage.Content.ReadAsStringAsync();
            var apiCommandResponse = JsonConvert.DeserializeObject<ApiCommandResponse>(machineNameResponse);
            string machineName = apiCommandResponse.Output;
            machineName = machineName.Replace(Environment.NewLine, "");
            _output.WriteLine("Machine Name is " + machineName);
            return machineName;
        }
    }

    class ApiCommandResponse
    {
        public string Output { get; set; }
        public string Error { get; set; }
        public int ExitCode { get; set; }
    }
}
