// -----------------------------------------------------------------------
// <copyright file="SessionTestHelpers.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DaaS.Sessions;
using Daas.Test;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Tests
{
    internal class SessionTestHelpers
    {
        internal static async Task<Session> SubmitNewSession(string diagnosticTool, HttpClient client, HttpClient webSiteClient, ITestOutputHelper outputHelper)
        {
            var warmupMessage = await EnsureSiteWarmedUpAsync(webSiteClient);
            outputHelper.WriteLine("Warmup message is: " + warmupMessage);
            var machineName = await GetMachineName(client, outputHelper);
            var newSession = new Session()
            {
                Mode = Mode.CollectAndAnalyze,
                Tool = diagnosticTool,
                Instances = new List<string> { machineName }
            };

            var response = await client.PostAsJsonAsync("daas/sessions", newSession);
            Assert.NotNull(response);

            Assert.Equal(System.Net.HttpStatusCode.Accepted, response.StatusCode);

            string sessionIdResponse = await response.Content.ReadAsStringAsync();
            Assert.NotNull(sessionIdResponse);

            outputHelper.WriteLine("SessionId Response is " + sessionIdResponse);

            string sessionId = JsonConvert.DeserializeObject<string>(sessionIdResponse);

            var session = await GetSessionInformation(sessionId, client);
            while (session.Status == Status.Active)
            {
                await Task.Delay(15000);
                session = await GetSessionInformation(sessionId, client);
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

        internal static async Task<Session> GetSessionInformation(string sessionId, HttpClient client)
        {
            var sessionResponse = await client.PostAsync($"daas/sessions/{sessionId}", null);
            sessionResponse.EnsureSuccessStatusCode();

            string sessionString = await sessionResponse.Content.ReadAsStringAsync();
            var session = JsonConvert.DeserializeObject<Session>(sessionString);
            return session;
        }

        internal static async Task RunProfilerTest(HttpClient client, HttpClient webSiteClient, ITestOutputHelper outputHelper)
        {
            var session = await SessionTestHelpers.SubmitNewSession("Profiler with Thread Stacks", client, webSiteClient, outputHelper);
            var log = session.ActiveInstances.FirstOrDefault().Logs.FirstOrDefault();
            Assert.Contains(".zip", log.Name);

            //
            // Just ensure that size returned is within 1kb - 100MB
            //

            long minFileSize = 1024; // 1kb
            long maxFileSize = 100 * 1024 * 1024; // 100MB
            Assert.InRange(log.Size, minFileSize, maxFileSize);
        }

        private static async Task<string> GetMachineName(HttpClient client, ITestOutputHelper outputHelper)
        {
            var machineResponseMessage = await client.PostAsJsonAsync("api/command", new { command = "hostname", dir = "site" });
            machineResponseMessage.EnsureSuccessStatusCode();

            string machineNameResponse = await machineResponseMessage.Content.ReadAsStringAsync();
            var apiCommandResponse = JsonConvert.DeserializeObject<ApiCommandResponse>(machineNameResponse);
            string machineName = apiCommandResponse.Output;
            machineName = machineName.Replace(Environment.NewLine, "");
            outputHelper.WriteLine("Machine Name is " + machineName);
            return machineName;
        }
    }
}
