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
            string userName = configuration["KUDU_USERNAME"];
            string password = configuration["KUDU_PASSWORD"];
            string kuduEndpoint = configuration["KUDU_ENDPOINT"];

            if (string.IsNullOrWhiteSpace(userName))
            {
                userName = Environment.GetEnvironmentVariable("KUDU_USERNAME");
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                password = Environment.GetEnvironmentVariable("KUDU_PASSWORD");
            }
            if (string.IsNullOrWhiteSpace(kuduEndpoint))
            {
                kuduEndpoint = Environment.GetEnvironmentVariable("KUDU_ENDPOINT");
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new ArgumentNullException(nameof(userName));
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentNullException(nameof(password));
            }
            if (string.IsNullOrWhiteSpace(kuduEndpoint))
            {
                throw new ArgumentNullException(nameof(kuduEndpoint));
            }

            Console.WriteLine("Kudu Endpoint = " + kuduEndpoint);
            Console.WriteLine("Kudu UserName = " + userName);

            if (!kuduEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                kuduEndpoint = $"https://{kuduEndpoint}";
            }

            var credentials = new NetworkCredential(userName, password);
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
