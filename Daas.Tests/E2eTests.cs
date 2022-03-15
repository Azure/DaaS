// -----------------------------------------------------------------------
// <copyright file="EndToEndTests.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Daas.Test
{
    public class E2eTests
    {
        private readonly HttpClient _client;

        public E2eTests()
        {
            var configuration = Setup.GetConfiguration();
            _client = Setup.GetHttpClient(configuration);
        }

        [Fact]
        public async Task GetAllSessions_ShouldReturnValidHttpResponse()
        {
            var resp = await _client.GetAsync("daas/sessions");
            Assert.True(resp.IsSuccessStatusCode);
        }
    }
}
