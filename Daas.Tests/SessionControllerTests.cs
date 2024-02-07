using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using DaaS.Sessions;
using DaaS.Storage;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;

namespace Daas.Test
{
    public class SessionControllerTests
    {
        private readonly ITestOutputHelper _output;

        public SessionControllerTests(ITestOutputHelper output)
        {
            TestHelpers.SetupTestEnvironment();
            _output = output;
        }

        [Fact]
        public async Task GetActiveSessionShouldReturnNull()
        {
            var storageService = new AzureStorageService();
            var sessionManager = new SessionManager(storageService);
            var azureStorageSessionManager = new AzureStorageSessionManager(storageService);
            var sessionsController = GetSessionController(sessionManager, azureStorageSessionManager);

            var result = await sessionsController.GetActiveSession() as OkNegotiatedContentResult<Session>;

            Assert.NotNull(result);
            if (result == null)
            {
                throw new NullReferenceException("result is null");
            }

            Assert.Null(result.Content);
        }

        [Fact]
        public async Task SubmitMockShouldReturnNewSession()
        {
            var storageService = new AzureStorageService();
            var sessionManager = new SessionManager(storageService);
            var azureStorageSessionManager = new AzureStorageSessionManager(storageService);
            var sessionsController = GetSessionController(sessionManager, azureStorageSessionManager);

            var newSession = new Session()
            {
                Mode = Mode.Collect,
                Tool = "Mock",
                Instances = new List<string> { Environment.MachineName }
            };

            var result = await sessionsController.SubmitNewSession(newSession) as ResponseMessageResult;
            Assert.True(result != null, "Result should not be null");

            _output.WriteLine("Response.StatusCode = " + result.Response.StatusCode.ToString());

            Assert.Equal(System.Net.HttpStatusCode.Accepted, result.Response.StatusCode);

            string sessionIdResponse = await result.Response.Content.ReadAsStringAsync();
            Assert.True(sessionIdResponse != null, "sessionIdResponse should not be null");

            _output.WriteLine("sessionIdResponse.= " + sessionIdResponse);

            string sessionId = JsonConvert.DeserializeObject<string>(sessionIdResponse);
            Assert.True(!string.IsNullOrEmpty(sessionId), "sessionId should not be null");
            _output.WriteLine("SessionId.= " + sessionId);

            await Task.Delay(1000);
            var activeSession = await sessionsController.GetActiveSession() as OkNegotiatedContentResult<Session>;
            Assert.True(activeSession != null, "activeSession should not be null");
            Assert.True(activeSession.Content != null, "activeSession.Content should not be null");

            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));

            _output.WriteLine("Session Content = " + JsonConvert.SerializeObject(activeSession.Content));

            await sessionManager.RunToolForSessionAsync(activeSession.Content, queueAnalysisRequest: false, cts.Token);

            var completedSession = await sessionsController.GetSession(sessionId) as OkNegotiatedContentResult<Session>;
            Assert.True(completedSession != null, "Completed session should not be NULL");
            Assert.True(completedSession.Content != null, "completedSession.Content should not be NULL");

            Assert.Equal(Status.Complete, completedSession.Content.Status);
        }

        private static DiagnosticsExtension.Controllers.SessionController GetSessionController(SessionManager sessionManager, IAzureStorageSessionManager azureStorageSessionManager)
        {
            return new DiagnosticsExtension.Controllers.SessionController(sessionManager, azureStorageSessionManager)
            {
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }
    }
}
