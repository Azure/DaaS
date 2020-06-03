using DiagnosticsExtension.Models;
using DaaS;
using DaaS.Diagnostics;
using DaaS.Sessions;
using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class InstancesController : ApiController
    {
        public IEnumerable<String> Get()
        {
            //SessionController sessionController = new SessionController();
            //return sessionController.GetAllInstances();

            List<String> retVal = new List<string>();
            retVal.Add("MediumDedicatedWebWorker_IN_0");
            retVal.Add("MediumDedicatedWebWorker_IN_1");
            retVal.Add("MediumDedicatedWebWorker_IN_2");

            return retVal;
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
