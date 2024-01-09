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
        private readonly ISessionManager _sessionManager;

        public SessionV2Controller(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
            _sessionManager.IncludeSasUri = true;
        }

        [HttpPut]
        [HttpPost]
        [Route("")]
        public async Task<IHttpActionResult> SubmitNewSession([FromBody] Session session)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(session.Description))
                {
                    session.Description = "InvokedViaDaasApi";
                }

                if (!_sessionManager.ShouldCollectOnCurrentInstance(session))
                {
                    return BadRequest("The session is not requested on the current instance");
                }

                string sessionId = await _sessionManager.SubmitNewSessionAsync(session, isV2Session: true);
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
            _sessionManager.StartDiagLauncher(args, sessionId, session.Description);
        }
    }
}
