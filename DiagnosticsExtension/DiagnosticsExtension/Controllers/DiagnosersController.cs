using DiagnosticsExtension.Models;
using DaaS.Diagnostics;
using DaaS.Sessions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class DiagnosersController : ApiController
    {
        public IEnumerable<String> Get()
        {
            SessionController sessionController = new SessionController();
            return sessionController.GetAllDiagnosers().Select(p => p.Name);
        }

        //// POST api/values
        //public void Post([FromBody]string value)
        //{
        //}

        //// PUT api/values/5
        //public void Put(int id, [FromBody]string value)
        //{
        //}

        //// DELETE api/values/5
        //public void Delete(int id)
        //{
        //}
    }
}