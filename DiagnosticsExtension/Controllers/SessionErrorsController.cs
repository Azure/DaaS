using DaaS.Sessions;
using System;
using System.Linq;
using System.Text;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class SessionErrorsController : ApiController
    {

        public string Get(string sessionId)
        {
            SessionController sessionController = new SessionController();

            Session session = sessionController.GetSessionWithId(new SessionId(sessionId));

            StringBuilder reportErrorsStringBuilder = new StringBuilder();
            StringBuilder logErrorsStringBuilder = new StringBuilder();
            var diagnoserSessions = session.GetDiagnoserSessions();
            foreach (DiagnoserSession diagSession in diagnoserSessions)
            {
                reportErrorsStringBuilder.Append(String.Join("\r\n", diagSession.GetAnalyzerErrors().Distinct().ToArray()));
                logErrorsStringBuilder.Append(String.Join("\r\n", diagSession.GetCollectorErrors().Distinct().ToArray()));
            }

            return String.Concat(reportErrorsStringBuilder.ToString(), logErrorsStringBuilder.ToString());
        }
    }
}
