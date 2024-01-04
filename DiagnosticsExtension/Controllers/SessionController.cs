// -----------------------------------------------------------------------
// <copyright file="SessionController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using DaaS.Sessions;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("sessions")]
    public class SessionController : ApiController
    {
        private readonly ISessionManager _sessionManager;

        public SessionController(ISessionManager sessionManager)
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

                string sessionId = await _sessionManager.SubmitNewSessionAsync(session);
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

        [HttpPost]
        [Route("list")]
        public async Task<IHttpActionResult> ListSessions()
        {
            return Ok(await _sessionManager.GetAllSessionsAsync(isDetailed: true));
        }

        [HttpPost]
        [Route("active")]
        public async Task<IHttpActionResult> GetActiveSession()
        {
            var activeSession = await _sessionManager.GetActiveSessionAsync(isDetailed: true);
            if (activeSession == null)
            {
                activeSession = await _sessionManager.GetActiveSessionAsync(isDetailed: true);
            }

            return Ok(activeSession);
        }

        [HttpPost]
        [Route("{sessionId}")]
        public async Task<IHttpActionResult> GetSession(string sessionId)
        {
            var session = await _sessionManager.GetSessionAsync(sessionId, isDetailed: true);
            if (session != null)
            {
                return Ok(session);
            }

            return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.NotFound, $"Cannot find session with Id - {sessionId}"));
        }

        [Route("{sessionId}")]
        [HttpDelete]
        public async Task<IHttpActionResult> DeleteSession(string sessionId)
        {
            try
            {
                if (_sessionManager.IsSessionExisting(sessionId))
                {
                    await _sessionManager.DeleteSessionAsync(sessionId);
                    return Ok($"Session {sessionId} deleted successfully");
                }

                if (_sessionManager.IsSessionExisting(sessionId))
                {
                    await _sessionManager.DeleteSessionAsync(sessionId);
                    return Ok($"Session {sessionId} deleted successfully");
                }

                return Content(HttpStatusCode.NotFound, $"Session with Id '{sessionId}' not found");
            }
            catch (Exception ex)
            {
                return ResponseMessage(Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message));
            }
        }

        [Route("validatestorageaccount")]
        [HttpPost]
        public async Task<IHttpActionResult> ValidateStorageAccount()
        {
            return Ok(await _sessionManager.ValidateStorageAccount());
        }

        [Route("updatestorageaccount")]
        [HttpPost]
        public async Task<IHttpActionResult> UpdateStorageAccount([FromBody] StorageAccount storageSettings)
        {
            return Ok(await _sessionManager.UpdateStorageAccount(storageSettings));
        }
    }
}
