using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Xml;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/Events")]
    public class EventsController : ApiController
    {
        public string EventLogFile = Path.Combine(Environment.GetEnvironmentVariable("HOME"), "Logfiles" , "eventlog.xml");
        public string EventLogFilePath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), "Logfiles");

        public async Task<HttpResponseMessage> Get()
        {
            var events = new List<ServerSideEvent>();
            try
            {
                events = await EventLogParser.GetEvents(EventLogFile);
                return Request.CreateResponse(HttpStatusCode.OK, events);
            }
            catch (XmlException)
            {
                RenameEventLogFile();
                return Request.CreateResponse(HttpStatusCode.OK, events);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }

        }

        void RenameEventLogFile()
        {
            try
            {
                string newFileName = string.Format("eventlog{0}.xml", DateTime.UtcNow.ToString("ddMMyyyyhhmmss"));
                DaaS.Logger.LogVerboseEvent($"Moving corrupted EventLog file to {newFileName}");
                File.Move(EventLogFile, Path.Combine(EventLogFilePath, newFileName));
                DaaS.Logger.LogVerboseEvent($"EventLog renamed to {newFileName}");
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Failed in renaming event log file", ex);
            }
        }

    }
}
