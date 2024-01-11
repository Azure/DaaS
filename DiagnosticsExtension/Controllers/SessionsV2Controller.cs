// -----------------------------------------------------------------------
// <copyright file="SessionV2Controller.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using DaaS;
using DaaS.Sessions;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("sessionsv2")]
    public class SessionV2Controller : ApiController
    {
        private readonly IAzureStorageSessionManager _azureStorageSessionManager;

        public SessionV2Controller(IAzureStorageSessionManager azureStorageSessionManager)
        {
            _azureStorageSessionManager = azureStorageSessionManager;
            _azureStorageSessionManager.IncludeSasUri = true;
        }

        [HttpPut]
        [HttpPost]
        [Route("")]
        public async Task<IHttpActionResult> SubmitNewSession([FromBody] Session session)
        {
            try
            {
                if (_azureStorageSessionManager.IsEnabled == false)
                {
                    throw new ArgumentException("The App setting 'WEBSITE_DAAS_STORAGE_CONNECTIONSTRING' does not exist");
                }

                if (string.IsNullOrWhiteSpace(session.Description))
                {
                    session.Description = "InvokedViaDaasApi";
                }

                if (!_azureStorageSessionManager.ShouldCollectOnCurrentInstance(session))
                {
                    return BadRequest("The session is not requested on the current instance");
                }

                string sessionId = await _azureStorageSessionManager.SubmitNewSessionAsync(session);
                StartDiagLauncher(session, sessionId);
                return ResponseMessage(Request.CreateResponse(HttpStatusCode.Accepted, sessionId));
            }
            catch (ArgumentException argEx)
            {
                return BadRequest(argEx.Message);
            }
            catch (AccessViolationException aex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.Conflict, aex.Message));
            }
            catch (Exception ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message));
            }
        }

        private void StartDiagLauncher(Session session, string sessionId)
        {
            Logger.LogSessionVerboseEvent($"Starting DiagLauncher for session on instance {Environment.MachineName}", sessionId);
            string args = $" --sessionId {sessionId}";
            _azureStorageSessionManager.StartDiagLauncher(args, sessionId, session.Description);
        }
    }
}
