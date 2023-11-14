// -----------------------------------------------------------------------
// <copyright file="HyperVSessionManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using Newtonsoft.Json;
using System.Collections;

namespace DaaS.Sessions
{
    public class HyperVSessionManager : ISessionManager
    {
        public bool IncludeSasUri { get; set; }
        public bool InvokedViaAutomation { get; set; }

        private string daasHost;
        private string daasPort;

        private string baseUri;
        public HyperVSessionManager() 
        {
            daasHost = GetIpAddress();
            daasPort = GetDaasPort();
            if (string.IsNullOrEmpty(daasHost) || string.IsNullOrEmpty(daasPort) )
            {
                throw new ArgumentException("Unable to fetch daas host and IP");
            } else
            {
                    baseUri = $"http://{daasHost}:{daasPort}/sessions";
            }
        }

        private static string GetIpAddress()
        {
            IDictionary environmentVariables = System.Environment.GetEnvironmentVariables();
            string containerAddress = (string)environmentVariables["DAAS_HOST"];
            return containerAddress ?? string.Empty;
        }

        private static string GetDaasPort()
        {
            IDictionary environmentVariables = System.Environment.GetEnvironmentVariables();
            string containerAddress = (string)environmentVariables["DAAS_PORT"];
            return containerAddress ?? string.Empty;
        }

        private static readonly Lazy<HttpClient> client = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }
        );
        private HttpClient httpClient
        {
            get
            {
                return client.Value;
            }
        }
       
        private TimeSpan timeout = TimeSpan.FromSeconds(60);

        Task ISessionManager.CancelOrphanedInstancesIfNeeded()
        {
            throw new NotImplementedException();
        }

        Task<bool> ISessionManager.CheckandCompleteSessionIfNeededAsync(bool forceCompletion)
        {
            throw new NotImplementedException();
        }

        public async Task DeleteSessionAsync(string sessionId)
        {
            await Task.Run(async () =>
            {
                await InvokeDiagServer<string>($"{baseUri}/{sessionId}", null, HttpMethod.Delete);
            });
            
        }

        public async Task<Session> GetActiveSessionAsync(bool isDetailed)
        {
            var response = await InvokeDiagServer<string>($"{baseUri}/active", null, HttpMethod.Get);
            return JsonConvert.DeserializeObject<Session>(response);
        }

        public async Task<IEnumerable<Session>> GetAllSessionsAsync(bool isDetailed)
        {
            var response = await InvokeDiagServer<string>(baseUri, null, httpMethod: HttpMethod.Get);
            return JsonConvert.DeserializeObject<IEnumerable<Session>>(response);
        }

        Task<IEnumerable<Session>> ISessionManager.GetCompletedSessionsAsync()
        {
            throw new NotImplementedException();
        }

        List<DiagnoserDetails> ISessionManager.GetDiagnosers()
        {
            throw new NotImplementedException();
        }

        public async Task<Session> GetSessionAsync(string sessionId, bool isDetailed)
        {
            var response = await InvokeDiagServer<string>($"{baseUri}/{sessionId}", null, HttpMethod.Get);
            return JsonConvert.DeserializeObject<Session>(response);
        }

        Task<bool> ISessionManager.HasThisInstanceCollectedLogs()
        {
            throw new NotImplementedException();
        }

        bool ISessionManager.IsSandboxAvailable()
        {
            throw new NotImplementedException();
        }

        Task ISessionManager.RunToolForSessionAsync(Session activeSession, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        bool ISessionManager.ShouldCollectOnCurrentInstance(Session activeSession)
        {
            throw new NotImplementedException();
        }

        public async Task<string> SubmitNewSessionAsync(Session session, bool invokedViaDaasConsole = false)
        {
            return await InvokeDiagServer<string>(baseUri, session, httpMethod: HttpMethod.Post);
        }

        private async Task<T> InvokeDiagServer<T>(string requestUri, object body = null, HttpMethod httpMethod = null)
        {
            HttpMethod requestMethod = httpMethod == null ? HttpMethod.Post : httpMethod;
            HttpRequestMessage requestMessage = new HttpRequestMessage(requestMethod, requestUri);

            if (body != null)
            {
                requestMessage.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            }
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource(timeout);
            HttpResponseMessage responseMessage = await httpClient.SendAsync(requestMessage, cancellationTokenSource.Token);
            object responseContent = await responseMessage.Content.ReadAsStringAsync();
            try
            {
                responseMessage.EnsureSuccessStatusCode();
                if (typeof(T).Equals(typeof(string)))
                {
                    return (T)(responseContent);
                }
                else
                {
                    object res = responseMessage;
                    return (T)res;
                }
            } catch (HttpRequestException ex)
            {
                ex.Data.Add("StatusCode", responseMessage.StatusCode);
                ex.Data.Add("ResponseContent", responseContent);
                throw;
            }
           
        }

        public async Task<StorageAccountValidationResult> ValidateStorageAccount()
        {
            var response = await InvokeDiagServer<string>($"{baseUri}/validatestorageaccount", null, HttpMethod.Get);
            return JsonConvert.DeserializeObject<StorageAccountValidationResult>(response);
        }

        public async Task<bool> UpdateStorageAccount(StorageAccount storageAccount)
        {
           var response = await InvokeDiagServer<string>($"{baseUri}/updatestorageaccount", storageAccount, HttpMethod.Post);
           return JsonConvert.DeserializeObject<bool>(response);      
        }
    }
}
