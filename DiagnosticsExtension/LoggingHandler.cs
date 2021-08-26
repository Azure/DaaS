// -----------------------------------------------------------------------
// <copyright file="LoggingHandler.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DaaS;

namespace DiagnosticsExtension
{
    public class LoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var watch = new Stopwatch();

            watch.Start();
            var response = await base.SendAsync(request, cancellationToken);
            watch.Stop();

            var statusCode = (int)response.StatusCode;

            if (statusCode > 399 || watch.ElapsedMilliseconds > 3000)
            {
                var responseString = string.Empty;

                if (statusCode > 399)
                {
                    responseString = await response.Content.ReadAsStringAsync();
                }
                Logger.LogApiStatus(request.RequestUri.PathAndQuery, request.Method.ToString(), statusCode, watch.ElapsedMilliseconds, responseString);
            }

            return response;
        }
    }

}
