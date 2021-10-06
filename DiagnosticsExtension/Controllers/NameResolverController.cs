// -----------------------------------------------------------------------
// <copyright file="NameResolverController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    /// <summary>
    /// This controller simply does a DNS lookup on the worker and returns its result. This is a replacement of nameresolver CLI as it sometimes behaves inconsistently 
    /// as the name resolving being done in the user code. Mainly used by Network Troubleshooter.
    /// </summary>
    [RoutePrefix("api/nameresolver")]
    public class NameResolverController : ApiController
    {
        public async Task<HttpResponseMessage> Get(string hostname, bool includeIpV6 = false)
        {
            HttpStatusCode httpStatus = HttpStatusCode.OK;
            NameResolverResult result = null;
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(hostname);
                var ips = hostEntry.AddressList.Where(addr => includeIpV6 || addr.AddressFamily != AddressFamily.InterNetworkV6).Select(addr => addr.ToString()).Distinct().ToList();
                result = new NameResolverResult { Status = "success", IpAddresses = ips };
                
            }
            catch (Exception e)
            {
                var socketException = e as SocketException;
                if (socketException != null && socketException.SocketErrorCode == SocketError.HostNotFound)
                {
                    result = new NameResolverResult { Status = "host not found" };
                }
                else
                {
                    result = new NameResolverResult { Status = "error", SocketError = socketException?.SocketErrorCode.ToString(), Exception = e };
                    httpStatus = HttpStatusCode.InternalServerError;
                }
            }

            return Request.CreateResponse(httpStatus, result);
        }

        private class NameResolverResult
        {
            public string Status;
            public List<string> IpAddresses;
            public Exception Exception;
            public string SocketError;
        }
    }
}
