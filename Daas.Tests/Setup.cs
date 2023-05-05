// -----------------------------------------------------------------------
// <copyright file="Setup.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
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
            string authToken = configuration["KUDU_AUTH_TOKEN"];
            string kuduEndpoint = configuration["KUDU_ENDPOINT"];

            if (string.IsNullOrWhiteSpace(authToken))
            {
                throw new ArgumentNullException(nameof(authToken));
            }
            if (string.IsNullOrWhiteSpace(kuduEndpoint))
            {
                throw new ArgumentNullException(nameof(kuduEndpoint));
            }

            if (!kuduEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                kuduEndpoint = $"https://{kuduEndpoint}";
            }

            
            var handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(kuduEndpoint),
            };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }
    }
}
