using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using DaaS.Sessions;
using DiagnosticsExtension.Controllers;
using Newtonsoft.Json;
using Xunit;

namespace Daas.Test
{
    public class SessionControllerTests
    {
        public SessionControllerTests()
        {
            TestHelpers.SetupTestEnvironment();
        }

        [Fact]
        public async Task GetActiveSession_ShouldReturnNullResponse()
        {
            var sessionManager = new SessionManager();
            var sessionsController = GetSessionController(sessionManager);
            var result = await sessionsController.GetActiveSession() as OkNegotiatedContentResult<Session>;

            Assert.NotNull(result);
            if (result == null)
            {
                throw new NullReferenceException("result is null");
            }

            Assert.Null(result.Content);
        }

        //[Fact]
        //public async Task SubmitNewMockSession_ShouldReturnNewSessionResponse()
        //{
        //    var sessionManager = new SessionManager();
        //    var sessionsController = GetSessionController(sessionManager);
        //    var newSession = new Session()
        //    {
        //        Mode = Mode.Collect,
        //        Tool = "Mock",
        //        Instances = new List<string> { Environment.MachineName }
        //    };

        //    var result = await sessionsController.SubmitNewSession(newSession) as ResponseMessageResult;
        //    Assert.NotNull(result);

        //    if (result == null)
        //    {
        //        throw new NullReferenceException("result is null");
        //    }

        //    Assert.Equal(System.Net.HttpStatusCode.Accepted, result.Response.StatusCode);

        //    string sessionIdResponse = await result.Response.Content.ReadAsStringAsync();
        //    Assert.NotNull(sessionIdResponse);

        //    string sessionId = JsonConvert.DeserializeObject<string>(sessionIdResponse);

        //    var activeSession = await sessionsController.GetActiveSession() as OkNegotiatedContentResult<Session>;
        //    Assert.NotNull(activeSession);

        //    var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        //    if (activeSession == null)
        //    {
        //        throw new NullReferenceException("activeSession is null");
        //    }    
            
        //    await sessionManager.RunToolForSessionAsync(activeSession.Content, cts.Token);

        //    var completedSession = await sessionsController.GetSession(sessionId) as OkNegotiatedContentResult<Session>;
        //    Assert.NotNull(completedSession);
            
        //    if (completedSession == null || completedSession.Content == null)
        //    {
        //        throw new NullReferenceException("Either completed session is null or content is null");
        //    }

        //    Assert.NotNull(completedSession.Content);
        //    Assert.Equal(Status.Complete, completedSession.Content.Status);
        //}

        private static DiagnosticsExtension.Controllers.SessionController GetSessionController(SessionManager sessionManager)
        {
            return new DiagnosticsExtension.Controllers.SessionController(sessionManager)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }
    }
}
