// -----------------------------------------------------------------------
// <copyright file="UdpEchoTestController.cs" company="Microsoft Corporation">
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
    /// Worker instances are running an udp echo server on port 30000. This controller is for checking the connection between target 
    /// worker instance by pinging and checking the echoed result.
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
                if (socketException!=null && socketException.SocketErrorCode == SocketError.HostNotFound)
                {
                    result = new NameResolverResult { Status = "host not found" };
                }
                else
                {
                    result = new NameResolverResult { Status = "unknown error", Exception = e };
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
        }
    }
}
