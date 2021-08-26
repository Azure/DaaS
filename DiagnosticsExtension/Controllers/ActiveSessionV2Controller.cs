using System.Threading.Tasks;
using System.Web.Http;
using DaaS.V2;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("activesession")]
    public class ActiveSessionV2Controller : ApiController
    {
        private readonly ISessionManager _sessionManager;
        public ActiveSessionV2Controller(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
            _sessionManager.IncludeSasUri = true;
        }

        
        [HttpGet]
        public async Task<IHttpActionResult> GetActiveSession()
        {
            return Ok(await _sessionManager.GetActiveSessionAsync(isDetailed: true));
        }
    }
}
