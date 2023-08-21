// -----------------------------------------------------------------------
// <copyright file="AdminController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("admin")]
    public class AdminController : ApiController
    {
        private const string DaasWebJobLogPath = @"%HOME%\Data\jobs\continuous\DaaS\job_log.txt";

        [HttpPost]
        [Route("startsessionrunner")]
        public IHttpActionResult StartSessionController()
        {
            try
            {
                var sessionController = new DaaS.Sessions.SessionController();
                sessionController.StartSessionRunner();
                return Ok("Session Started");
            }
            catch (Exception ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.ToString()));
            }
        }

        [HttpGet]
        [Route("getdaaswebjoblogs")]
        public async Task<IHttpActionResult> GetDaasWebJobLogs(int fileBufferSize = 8192)
        {
            try
            {
                string webjobLogPath = Environment.ExpandEnvironmentVariables(DaasWebJobLogPath);
                if (!File.Exists(webjobLogPath))
                {
                    return Ok($"File '{webjobLogPath}' does not exist");
                }

                string webjobLog = await ReadLastBytesAsync(webjobLogPath, fileBufferSize);
                return Ok(webjobLog);
            }
            catch (Exception ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.ToString()));
            }
        }

        private static async Task<string> ReadLastBytesAsync(string filePath, int bufferSize)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                long fileSize = fs.Length;
                int bytesToRead = (int)Math.Min(bufferSize, fileSize);
                byte[] buffer = new byte[bytesToRead];

                long startPosition = fileSize - bytesToRead;
                fs.Seek(startPosition, SeekOrigin.Begin);

                int bytesRead = await fs.ReadAsync(buffer, 0, bytesToRead);
                return Encoding.UTF8.GetString(buffer);
            }
        }
    }
}
