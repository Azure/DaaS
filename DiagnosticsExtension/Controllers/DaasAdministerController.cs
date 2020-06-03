using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class DaasAdministerController : ApiController
    {
        [HttpGet]
        public string StartSessionController(bool shouldStop = false)
        {
            try
            {
                SessionController sessionController = new SessionController();
                sessionController.StartSessionRunner();
            }
            catch (Exception ex)
            {
                HttpResponseMessage resp = new HttpResponseMessage();
                resp.StatusCode = HttpStatusCode.InternalServerError;
                resp.Content = new StringContent(ex.Message);
                throw new HttpResponseException(resp);
            }
            if (shouldStop)
            {
                //TODO
            }
            return "Session Started";
        }
    }
}
