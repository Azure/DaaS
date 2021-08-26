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
using DaaS.V2;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("sessions")]
    public class SessionV2Controller : ApiController
    {
        private readonly ISessionManager _sessionManager;

        public SessionV2Controller(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
            _sessionManager.IncludeSasUri = true;
        }

        [HttpPost]
        public async Task<IHttpActionResult> SubmitNewSession([FromBody] Session session)
        {
            try
            {
                string sessionId = await _sessionManager.SubmitNewSessionAsync(session);
                return Ok(sessionId);
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

        [HttpGet]
        public async Task<IHttpActionResult> GetSessions()
        {
            return Ok(await _sessionManager.GetAllSessionsAsync(isDetailed: true));
        }

        [Route("{sessionId}")]
        [HttpGet]
        public async Task<IHttpActionResult> GetSession(string sessionId)
        {
            return Ok(await _sessionManager.GetSessionAsync(sessionId));
        }

        [Route("{sessionId}")]
        [HttpDelete]
        public async Task<IHttpActionResult> DeleteSession(string sessionId)
        {
            try
            {
                await _sessionManager.DeleteSessionAsync(sessionId);
                return Ok();
            }
            catch (Exception ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message));
            }
        }
    }
}
