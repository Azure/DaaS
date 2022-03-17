// -----------------------------------------------------------------------
// <copyright file="EndToEndTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Threading.Tasks;
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
                var resp = await _client.GetAsync($"api/vfs/siteextensions/daas/{file}");
                Assert.True(resp.IsSuccessStatusCode);
            }
        }
    }
}
