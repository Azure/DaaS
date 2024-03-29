﻿// -----------------------------------------------------------------------
// <copyright file="SessionTestHelpers.cs" company="Microsoft Corporation">
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
using System.Text.RegularExpressions;
using DaaS;

namespace Daas.Test
{
    internal class SessionTestHelpers
    {
        const string SessionFileNameFormat = "yyMMdd_HHmmssffff";
        static int _requestCounter = 0;

        internal static async Task<Session> SubmitNewSession(string diagnosticTool, HttpClient client, HttpClient webSiteClient, ITestOutputHelper outputHelper, string webSiteInstances, List<string> requestedInstances, bool isV2Session = false)
        {
            if (isV2Session && string.IsNullOrWhiteSpace(webSiteInstances))
            {
                throw new ArgumentNullException(nameof(webSiteInstances));
            }

            var warmupMessage = await EnsureSiteWarmedUpAsync(webSiteClient);
            outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Warmup message is: " + warmupMessage);
            var machineName = await GetMachineName(client, outputHelper);

            if (!string.IsNullOrWhiteSpace(webSiteInstances))
            {
                outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Instances = {webSiteInstances}");
            }

            var newSession = new Session()
            {
                Mode = Mode.CollectAndAnalyze,
                Tool = diagnosticTool,
                Instances = requestedInstances.Count == 0 ? new List<string> { machineName } : requestedInstances
            };

            string sessionId = "";
            if (isV2Session)
            {
                newSession.SessionId = DateTime.UtcNow.ToString(SessionFileNameFormat);
                List<SiteInstance> siteInstances = JsonConvert.DeserializeObject<List<SiteInstance>>(webSiteInstances);

                var requestedSiteInstances = new List<SiteInstance>();

                foreach (var requestedInstance in newSession.Instances)
                {
                    var instance = siteInstances.FirstOrDefault(x => x.machineName.Equals(requestedInstance, StringComparison.OrdinalIgnoreCase));
                    Assert.NotNull(instance);
                    requestedSiteInstances.Add(instance);

                }

                foreach (var siteInstance in requestedSiteInstances)
                {
                    HttpResponseMessage response = null;
                    await RetryHelper.RetryOnExceptionAsync(3, TimeSpan.FromSeconds(1),
                        async () =>
                        {
                            outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Making HTTP POST call to submit new session to daas/sessionsV2?instance={siteInstance.siteInstanceName}");
                            response = await client.PostAsJsonAsync($"daas/sessionsV2?instance={siteInstance.siteInstanceName}", newSession);
                        });

                    Assert.True(response != null, $"HttpResponse object for posting V2 Session for {siteInstance.siteInstanceName} is NULL");

                    string sessionIdResponse = await response.Content.ReadAsStringAsync();
                    Assert.NotNull(sessionIdResponse);

                    string message = $"[{DateTime.UtcNow:O}] SessionId Response StatusCode is {response.StatusCode} and response body is {sessionIdResponse}";
                    outputHelper.WriteLine(message);
                    if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        sessionId = JsonConvert.DeserializeObject<string>(sessionIdResponse);
                    }
                    else
                    {
                        Assert.True(false, message);
                    }
                }
            }
            else
            {
                HttpResponseMessage response = null;
                await RetryHelper.RetryOnExceptionAsync(2, TimeSpan.FromSeconds(1),
                    async () =>
                    {
                        outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Making HTTP POST call to submit new session to daas/sessions");
                        response = await client.PostAsJsonAsync("daas/sessions", newSession);
                    });

                Assert.True(response != null, "HttpResponse object for posting Session is NULL");
                Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);

                string sessionIdResponse = await response.Content.ReadAsStringAsync();
                Assert.NotNull(sessionIdResponse);

                string message = $"[{DateTime.UtcNow:O}] SessionId Response StatusCode is {response.StatusCode} and response body is {sessionIdResponse}";
                outputHelper.WriteLine(message);
                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    sessionId = JsonConvert.DeserializeObject<string>(sessionIdResponse);
                }
                else
                {
                    Assert.True(false, message);
                }
            }

            await Task.Delay(15000);

            Session session = await GetSessionInformationAsync(sessionId, client, outputHelper);
            Assert.True(session != null, "Session should not be null");
            outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Session Status for {sessionId} is {session.Status}");

            while (session.Status == Status.Active)
            {
                await Task.Delay(20000);
                session = await GetSessionInformationAsync(sessionId, client, outputHelper);
                Assert.NotNull(session);
            }

            CheckSessionAsserts(session);

            return session;
        }

        internal static async Task<string> EnsureSiteWarmedUpAsync(HttpClient websiteClient)
        {
            int counter = 0;
            var siteResponse = await websiteClient.GetAsync("/");
            while (!siteResponse.IsSuccessStatusCode && counter < 10)
            {
                await Task.Delay(5000);
                siteResponse = await websiteClient.GetAsync("/");
                counter++;
            }

            var responseCode = siteResponse.StatusCode;
            string message = $"Site name is '{websiteClient.BaseAddress}' and Status Code returned is {responseCode}";
            Assert.True(siteResponse.IsSuccessStatusCode, $"Site '{websiteClient.BaseAddress}' is not warmed up. Status Code returned is {responseCode}");
            return message;
        }

        internal static void CheckSessionAsserts(Session session)
        {
            Assert.Equal(Status.Complete, session.Status);
            Assert.False(session.EndTime == DateTime.MinValue || session.StartTime == DateTime.MinValue, "Session EndTime should be populated properly");

            Assert.True(!string.IsNullOrWhiteSpace(session.Description), "Session must have a description");
            Assert.True(!string.IsNullOrWhiteSpace(session.DefaultScmHostName), "Session must have valid DefaultScmHostName");

            Assert.NotNull(session.ActiveInstances);
            Assert.NotEmpty(session.ActiveInstances);

            Assert.NotNull(session.ActiveInstances.FirstOrDefault().Logs);
            Assert.NotNull(session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault().Reports);
            Assert.NotNull(session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault().Reports.FirstOrDefault());

            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            var report = log.Reports.FirstOrDefault();

            Assert.NotNull(report.Name);
            Assert.NotNull(report.RelativePath);

            //
            // The logic for Utility.GetScmHostName() is a bit flaky. For now, lets live with this
            //

            Assert.True(report.RelativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || report.RelativePath.StartsWith("/api/vfs", StringComparison.OrdinalIgnoreCase), $"Report relativePath is not expected {report.RelativePath.Substring(0, 10)}");
            Assert.True(log.RelativePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || log.RelativePath.StartsWith("/api/vfs", StringComparison.OrdinalIgnoreCase), $"Log relativePath is not expected {log.RelativePath.Substring(0, 10)}");
        }

        internal static async Task<Session> GetSessionInformationAsync(string sessionId, HttpClient client, ITestOutputHelper testOutputHelper)
        {
            int retryCount = 5;

            while (retryCount > 0)
            {
                try
                {
                    testOutputHelper.WriteLine($"[{DateTime.UtcNow:O}] Retry Count = {retryCount}. Getting SessionId {sessionId}");
                    var sessionResponse = await client.PostAsync($"daas/sessions/{sessionId}", null);
                    testOutputHelper.WriteLine($"[{DateTime.UtcNow:O}] Retry Count = {retryCount}. Response Code is {sessionResponse.StatusCode}");
                    await LogResponseOutputAsync(sessionResponse, testOutputHelper);
                    sessionResponse.EnsureSuccessStatusCode();

                    string sessionString = await sessionResponse.Content.ReadAsStringAsync();
                    Assert.True(!string.IsNullOrWhiteSpace(sessionString), "Session Content should not be empty");
                    if (string.IsNullOrWhiteSpace(sessionString))
                    {
                        throw new Exception($"Retry Count = {retryCount}.Session response is empty");
                    }

                    var session = JsonConvert.DeserializeObject<Session>(sessionString);
                    if (session == null)
                    {
                        throw new Exception($"Retry Count = {retryCount}. Encountered NULL session while trying to deserialize '{sessionString}'");
                    }

                    return session;
                }
                catch (Exception ex)
                {
                    testOutputHelper.WriteLine($"[{DateTime.UtcNow:O}] Retry Count = {retryCount}. Encountered Exception = {ex}");
                    --retryCount;
                    if (retryCount == 0)
                    {
                        throw new Exception($"Passed maximum number of retries for Getting session infomration for {sessionId}. Ex = {ex}");
                    }
                }
            }

            return null;
        }

        internal static async Task<Session> RunProfilerTest(HttpClient client, HttpClient webSiteClient, ITestOutputHelper outputHelper, string webSiteInstances, List<string> requestedInstances, bool isV2Session = false)
        {
            var session = await SubmitNewSession("Profiler with Thread Stacks", client, webSiteClient, outputHelper, webSiteInstances, requestedInstances, isV2Session: isV2Session);
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(".zip", log.Name);

            //
            // Just ensure that size returned is within 1kb - 100MB
            //

            long minFileSize = 1024; // 1kb
            long maxFileSize = 100 * 1024 * 1024; // 100MB
            Assert.InRange(log.Size, minFileSize, maxFileSize);

            return session;
        }

        internal static async Task ValidateMemoryDumpAsync(Session session, HttpClient client)
        {
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(".dmp", log.Name);
            Assert.True(!string.IsNullOrWhiteSpace(session.BlobStorageHostName), "BlobStorageHostName should not be empty");

            //
            // Just ensure that size returned is within 50MB - 5GB
            //

            long minDumpSize = 52428800; // 50 MB
            long maxDumpSize = 5368709120; // 5GB
            Assert.InRange<long>(log.Size, minDumpSize, maxDumpSize);

            // simple sanity check that verifies the html report contains a reference to the dmp file (for the "Open in VS" scenario")
            Report htmlReport = log.Reports.FirstOrDefault(r => r.Name.EndsWith(".html"));
            Assert.NotNull(htmlReport);

            var htmlReportResponse = await client.GetAsync("api/vfs/" + htmlReport.PartialPath);
            Assert.True(htmlReportResponse.IsSuccessStatusCode, "Should be able to download the HTML report");

            var htmlReportContent = await htmlReportResponse.Content.ReadAsStringAsync();

            var dmpBlobUri = log.RelativePath.Split('?')[0];// remove the SAS token URL params
            Assert.True(htmlReportContent.Contains(dmpBlobUri), "The HTML report needs to contain a reference to the Azure Storage blob containing the dump.");

            var storageAccountName = session.BlobStorageHostName.Split('.')[0];
            var storageResourceIdRegex = new Regex($"/subscriptions/[a-z0-9\\-]+/resourceGroups/[\\w0-9\\-_\\(\\)\\.]+/providers/Microsoft\\.Storage/storageAccounts/{storageAccountName}");
            Assert.True(storageResourceIdRegex.IsMatch(htmlReportContent), "The HTML report needs to contain a reference to the Azure Storage resource id containing the dump.");
        }

        internal static async Task ValidateProfilerAsync(Session session, HttpClient client)
        {
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(".zip", log.Name);

            //
            // Just ensure that size returned is within 50MB - 5GB
            //

            long minDumpSize = 1024 * 1024; // 1MB
            long maxDumpSize = 100 * 1024 * 1024; // 100 MB
            Assert.InRange<long>(log.Size, minDumpSize, maxDumpSize);

            // Ensure that HTML report got created
            Report htmlReport = log.Reports.FirstOrDefault(r => r.Name.EndsWith(".html"));
            Assert.NotNull(htmlReport);

            var htmlReportResponse = await client.GetAsync("api/vfs/" + htmlReport.PartialPath);
            Assert.True(htmlReportResponse.IsSuccessStatusCode, "Should be able to download HTML report for Profiler session");

            var htmlReportContent = await htmlReportResponse.Content.ReadAsStringAsync();
            var hrefString = htmlReportContent.Split(Environment.NewLine.ToCharArray()).FirstOrDefault(x => x.Contains("window.location.href = "));

            Assert.False(string.IsNullOrWhiteSpace(hrefString), "Index.html should contain window.location.href script code");
        }

        internal static async Task<Session> GetActiveSessionAsync(HttpClient client, HttpClient webSiteClient, ITestOutputHelper outputHelper)
        {
            int retryCount = 5;
            while (retryCount > 0)
            {
                try
                {
                    var response = await client.PostAsJsonAsync("daas/sessions/active", string.Empty);
                    Assert.NotNull(response);
                    if (response.Content == null)
                    {
                        outputHelper.WriteLine($"[{DateTime.UtcNow:O}] retryCount = {retryCount}. response.Content is NULL");
                        return null;
                    }

                    string activeSessionResponse = await response.Content.ReadAsStringAsync();
                    outputHelper.WriteLine($"[{DateTime.UtcNow:O}] retryCount = {retryCount}. Get Active session at  {DateTime.UtcNow:O} and response code is {response.StatusCode} and responseBody is {activeSessionResponse}");

                    await LogResponseOutputAsync(response, outputHelper);
                    response.EnsureSuccessStatusCode();

                    var activeSession = JsonConvert.DeserializeObject<Session>(activeSessionResponse);
                    var sessionId = activeSession.SessionId;

                    Assert.True(!string.IsNullOrWhiteSpace(sessionId), "Session ID should not empty");

                    var session = await GetSessionInformationAsync(sessionId, client, outputHelper);
                    Assert.NotNull(session);
                    return session;
                }
                catch (Exception ex)
                {
                    --retryCount;
                    if (retryCount == 0)
                    {
                        throw new Exception($"GetActiveSessionAsync failed with {ex}");
                    }

                    await Task.Delay(2000);
                }
            }

            throw new Exception("GetActiveSessionAsync failed");
        }

        internal static async Task StressTestWebAppAsync(int requestCount, HttpClient webSiteClient, ITestOutputHelper outputHelper)
        {

            Task[] tasks = new Task[requestCount];

            for (int i = 0; i < requestCount; i++)
            {
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    MakeSiteRequest(webSiteClient, outputHelper);
                });
            }

            await Task.WhenAll(tasks);
        }

        private static void MakeSiteRequest(HttpClient webSiteClient, ITestOutputHelper outputHelper)
        {
            try
            {
                var response = webSiteClient.GetAsync("/").Result;
                outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Request completed with {response.StatusCode}");
            }
            catch (Exception ex)
            {
                outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Request failed with {ex}");
            }
        }

        private static async Task<string> GetMachineName(HttpClient client, ITestOutputHelper outputHelper)
        {
            int retryCount = 5;
            while (retryCount > 0)
            {
                try
                {
                    outputHelper.WriteLine($"[{DateTime.UtcNow:O}] Retry Count = {retryCount}. Getting Machine name");
                    var versionResponse = await client.GetAsync($"daas/api/v2/daasversion");

                    await LogResponseOutputAsync(versionResponse, outputHelper);
                    versionResponse.EnsureSuccessStatusCode();

                    string versionResponseString = await versionResponse.Content.ReadAsStringAsync();
                    var versionInfo = JsonConvert.DeserializeObject<DaasVersionResponse>(versionResponseString);
                    return versionInfo.Instance;
                }
                catch (Exception ex)
                {
                    --retryCount;
                    if (retryCount == 0)
                    {
                        throw new Exception($"GetMachineName failed with {ex}");
                    }
                }
            }

            throw new Exception("GetMachineName failed");
        }

        internal static async Task EnsureDiagLauncherFinishedAsync(HttpClient client, ITestOutputHelper outputHelper)
        {
            bool diagLauncherRunning = true;
            do
            {
                await Task.Delay(10000);
                var processesResponseMessage = await client.GetAsync("api/processes");
                await LogResponseOutputAsync(processesResponseMessage, outputHelper);
                processesResponseMessage.EnsureSuccessStatusCode();

                var processesResponse = await processesResponseMessage.Content.ReadAsStringAsync();
                var processes = JsonConvert.DeserializeObject<KuduProcessEntry[]>(processesResponse);
                diagLauncherRunning = processes.Any(p => p.name.ToLower().Contains("diaglauncher"));
                outputHelper.WriteLine($"[{DateTime.UtcNow:O}] DiagLauncherRunning = {diagLauncherRunning}");
            }
            while (diagLauncherRunning);
        }

        private static async Task LogResponseOutputAsync(HttpResponseMessage response, ITestOutputHelper testOutputHelper)
        {
            if (response != null && response.Content != null)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                testOutputHelper.WriteLine($"[{DateTime.UtcNow:O}] Response Status is {response.StatusCode} and response Content is {responseString}");
            }
        }
    }


    public class SiteInstance
    {
        public string state { get; set; }
        public string name { get; set; }
        public string siteInstanceName { get; set; }
        public string statusUrl { get; set; }
        public string detectorUrl { get; set; }
        public string consoleUrl { get; set; }
        public string healthCheckUrl { get; set; }
        public string machineName { get; set; }
        public object containers { get; set; }
    }


    internal class KuduProcessEntry
    {
        public int id { get; set; }
        public string name { get; set; }
        public string machineName { get; set; }
        public string href { get; set; }
        public string user_name { get; set; }
    }


    public class DaasVersionResponse
    {
        public string Version { get; set; }
        public bool IsDaasRunnerRunning { get; set; }
        public bool DaasWebJobStoppped { get; set; }
        public bool DaasWebJobDisabled { get; set; }
        public DateTime DaasRunnerStartDate { get; set; }
        public string Instance { get; set; }
        public string DaasConsoleVersion { get; set; }
        public string DaasRunnerVersion { get; set; }
    }

}
