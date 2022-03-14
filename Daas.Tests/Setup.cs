// -----------------------------------------------------------------------
// <copyright file="Setup.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;

namespace Daas.Test
{
    internal class Setup
    {
        internal static IConfiguration GetConfiguration()
        {
            var config = new ConfigurationBuilder()
               .AddJsonFile("appsettings.json")
               .AddEnvironmentVariables()
               .Build();

            return config;
        }

        internal static HttpClient GetHttpClient(IConfiguration configuration)
        {
            string kuduEndpoint = configuration["KUDU_ENDPOINT"];
            if (!kuduEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                kuduEndpoint = $"https://{kuduEndpoint}";
            }

            var credentials = new NetworkCredential(configuration["KUDU_USERNAME"], configuration["KUDU_PASSWORD"]);
            var handler = new HttpClientHandler { Credentials = credentials };

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(kuduEndpoint),
            };

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }
    }
}
