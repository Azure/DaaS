using DaaS.V2;
using DiagnosticsExtension.Controllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace Daas.Tests
{
    [TestClass]
    public class TestSessionsController
    {
        private SessionV2Controller GetSessionController(SessionManager sessionManager)
        {
            return new SessionV2Controller(sessionManager)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        [TestInitialize]
        public void InitializeTest()
        {
            TestHelpers.SetupTestEnvironment();
        }

        [TestMethod]
        public async Task GetActiveSession_ShouldReturnNullResponse()
        {
            var sessionManager = new SessionManager();
            var sessionsController = GetSessionController(sessionManager);
            var result = await sessionsController.GetActiveSession() as OkNegotiatedContentResult<Session>;
            Assert.IsNull(result.Content);
        }

        [TestMethod]
        public async Task SubmitNewProfilerSession_ShouldReturnNewSessionResponse()
        {
            var sessionManager = new SessionManager();
            var sessionsController = GetSessionController(sessionManager);
            var newSession = new Session()
            {
                Mode = Mode.Collect,
                Tool = "Profiler",
                Instances = new List<string> { Environment.MachineName}
            };

            var response = await sessionsController.SubmitNewSession(newSession);
            Assert.IsNotNull(response);
            
//            NegotiatedContentResult<string> negResult = Assert.IsInstanceOfType<response, );
        }
    }
}
