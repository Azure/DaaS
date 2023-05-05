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
            string userName = string.Empty;
            string password = string.Empty;
            bool usingPublishProfileCredentails = false;

            if (string.IsNullOrWhiteSpace(kuduEndpoint))
            {
                throw new ArgumentNullException(nameof(kuduEndpoint));
            }

            if (string.IsNullOrWhiteSpace(authToken))
            {
                userName = configuration["KUDU_USERNAME"];
                password = configuration["KUDU_PASSWORD"];

                if (string.IsNullOrWhiteSpace(userName))
                {
                    throw new ArgumentNullException(nameof(userName));
                }
                if (string.IsNullOrWhiteSpace(password))
                {
                    throw new ArgumentNullException(nameof(password));
                }

                usingPublishProfileCredentails = true;
            }

            if (!kuduEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                kuduEndpoint = $"https://{kuduEndpoint}";
            }

            HttpClientHandler handler = new HttpClientHandler();
            if (usingPublishProfileCredentails)
            {
                var credentials = new NetworkCredential(userName, password);
                handler.Credentials = credentials;
            }

            handler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(kuduEndpoint),
            };

            if (!usingPublishProfileCredentails)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
            }

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return client;
        }
    }
}
