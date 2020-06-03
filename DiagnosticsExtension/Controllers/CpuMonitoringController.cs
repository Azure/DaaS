using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using DaaS;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/CpuMonitoring")]
    public class CpuMonitoringController : ApiController
    {
        // GET: api/CpuMonitoring
        public HttpResponseMessage Get()
        {
            var monitoringController = new MonitoringSessionController();
            try
            {
                var sessions = monitoringController.GetAllCompletedSessions();
                return Request.CreateResponse(HttpStatusCode.OK, sessions);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - Get", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        // GET: api/CpuMonitoring/5
        public HttpResponseMessage Get(string sessionId)
        {
            var monitoringController = new MonitoringSessionController();
            try
            {
                var session = monitoringController.GetSession(sessionId);
                return Request.CreateResponse(HttpStatusCode.OK, session);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - GetSessionId", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, ex.Message);
            }
        }

        // GET: api/CpuMonitoring/active
        [HttpGet]
        [Route("active")]
        public HttpResponseMessage GetActiveSession()
        {
            var monitoringController = new MonitoringSessionController();
            try
            {
                var session = monitoringController.GetActiveSession();
                return Request.CreateResponse(HttpStatusCode.OK, session);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - GetActiveSession", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, ex.Message);
            }
        }

        // GET: api/CpuMonitoring/monitoringlogs
        [HttpGet]
        [Route("activesessiondetails")]
        public HttpResponseMessage GetActiveSessionDetails()
        {
            ActiveMonitoringSession activeSession = new ActiveMonitoringSession();
            var monitoringController = new MonitoringSessionController();
            try
            {
                activeSession.Session = monitoringController.GetActiveSession();
                if (activeSession.Session != null)
                {
                    var sessionLogs = monitoringController.GetActiveSessionMonitoringLogs();
                    activeSession.MonitoringLogs = sessionLogs.ToList();
                    activeSession.Session.FilesCollected = monitoringController.GetCollectedLogsForSession(activeSession.Session.SessionId, activeSession.Session.BlobSasUri);
                }

                return Request.CreateResponse(HttpStatusCode.OK, activeSession);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - GetActiveSessionDetails", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, ex.Message);
            }
        }

        // POST: api/CpuMonitoring/stop
        [HttpPost]
        [Route("stop")]
        public HttpResponseMessage StopMonitoringSession()
        {
            var monitoringController = new MonitoringSessionController();
            try
            {
                var monitoringSessionStopped = monitoringController.StopMonitoringSession();
                return Request.CreateResponse(HttpStatusCode.OK, monitoringSessionStopped);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - StopMonitoringSession", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        // POST: api/CpuMonitoring
        public HttpResponseMessage Post([FromBody]MonitoringSession session)
        {
            var monitoringController = new MonitoringSessionController();
            try
            {
                var submittedSession = monitoringController.CreateSession(session);
                return Request.CreateResponse(HttpStatusCode.OK, session.SessionId);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - POST", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, ex.Message);
            }
        }

        // DELETE: api/CpuMonitoring/5
        public HttpResponseMessage Delete(string sessionId)
        {
            var monitoringController = new MonitoringSessionController();
            try
            {
                monitoringController.DeleteSession(sessionId);
                return Request.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - DELETE", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.Conflict, ex.Message);
            }
        }

        [HttpPost]
        [Route("analyze")]
        public HttpResponseMessage Analyze(string sessionId)
        {
            var monitoringController = new MonitoringSessionController();
            var session = monitoringController.GetSession(sessionId);
            try
            {
                var result = monitoringController.AnalyzeSession(sessionId, session.BlobSasUri);
                return Request.CreateResponse(HttpStatusCode.OK, result);
            }
            catch (Exception ex)
            {
                Logger.LogCpuMonitoringErrorEvent("Controller API Failure - Analyze", ex, string.Empty);
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, ex.Message);
            }
        }
    }
}
