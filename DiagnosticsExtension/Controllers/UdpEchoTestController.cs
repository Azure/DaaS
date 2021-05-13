//-----------------------------------------------------------------------
// <copyright file="UdpEchoTestController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/udpechotest")]
    public class UdpEchoTestController : ApiController
    {
        public async Task<HttpResponseMessage> Get(string ip, int count = 4, int timeoutInSec = 2)
        {
            // worker runs a udp echo service on port 30000
            int port = 30000;

            int success = 0;
            var exceptions = new List<Exception>();
            for (int i = 0; i < count; ++i)
            {
                Exception exception = null;
                var udpClient = new UdpClient();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutInSec));
                var task = new Func<Task>(async () =>
                {
                    try
                    {
                        udpClient.Connect(ip, port);
                        byte[] sendBytes = Encoding.ASCII.GetBytes("ping");
                        await udpClient.SendAsync(sendBytes, sendBytes.Length);

                        //IPEndPoint object will allow us to read datagrams sent from any source.
                        IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, 0);

                        // Blocks until a message returns on this socket from a remote host.
                        var recieved = await udpClient.ReceiveAsync();
                        if (recieved.Buffer.Length == 0)
                        {
                            throw new Exception("empty response");
                        }
                    }
                    catch (Exception e)
                    {
                        exception = e;
                    }
                })();
                await Task.WhenAny(timeoutTask, task);
                if (timeoutTask.IsCompleted)
                {
                    exceptions.Add(new Exception($"timeout after {timeoutInSec} seconds"));
                }
                else
                {
                    if (exception == null)
                    {
                        ++success;
                    }
                    else 
                    {
                        exceptions.Add(exception);
                    }
                }
                udpClient.Close();
            }
            return Request.CreateResponse(HttpStatusCode.OK, new { success, exceptions });
        }
    }
}