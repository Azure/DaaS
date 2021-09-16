// -----------------------------------------------------------------------
// <copyright file="MsiValidatorModels.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using System.Threading.Tasks;

namespace DiagnosticsExtension.Models
{
    public class TokenInformation
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("expires_on")]
        public string ExpiresOn { get; set; }

        [JsonProperty("resource")]
        public string Resource { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("client_id")]
        public string ClientId { get; set; }
    }

    public class GetTokenTestResult
    {
        public bool IsSuccessful { get; set; }

        public TokenInformation TokenInformation { get; set; }

        public AdalError ErrorDetails { get; set; }
    }

    public class AdalError
    {
        public string ExceptionMessage { get; set; }

        public string ErrorCode { get; set; }

        public List<string> ServiceErrorCodes { get; set; }

        public string InnerException { get; set; }

        public int StatusCode { get; set; }

        public string Message { get; set; }

        public string CorrelationId { get; set; }
    }

    public class ConnectivityResult
    {
        public ResourceType ResourceType { get; set; }

        public string Resource { get; set; }

        public bool IsSuccessful { get; set; }

        public string Response { get; set; }

        public ConnectivityResult(ResourceType resourceType, string resource = null)
        {
            IsSuccessful = false;
            Response = "";
            ResourceType = resourceType;
            Resource = resource;
        }
    }

    public class MsiValidatorTestResult
    {
        public readonly string MsiValidatorVersion;

        public bool MsiEnabled { get; set; }

        public GetTokenTestResult GetTokenTestResult { get; set; }

        public ConnectivityResult ConnectivityResult { get; set; }

        public MsiValidatorTestResult()
        {
            MsiValidatorVersion = "v1.0.0.0";
            GetTokenTestResult = new GetTokenTestResult();
            ConnectivityResult = new ConnectivityResult(ResourceType.Custom);
        }
    }

    public class MsiValidatorInput
    {
        public string ClientId { get; set; }

        [EnumDataType(typeof(ResourceType))]
        public ResourceType ResourceType { get; set; }

        public string Endpoint { get; set; }

        private string _resource { get; set; }

        public string Resource
        {
            get { return _resource; }
            set
            {
                switch (ResourceType)
                {
                    case ResourceType.KeyVault:
                        _resource = "https://vault.azure.net";
                        break;

                    case ResourceType.Storage:
                        _resource = "https://storage.azure.com";
                        break;

                    case ResourceType.Sql:
                        _resource = "https://database.windows.net";
                        break;

                    default:
                        _resource = value;
                        break;
                }
            }
        }

        public MsiValidatorInput(ResourceType resourceType, string resource = null, string endpoint = null, string clientId = null)
        {
            ResourceType = resourceType;
            Endpoint = endpoint;
            ClientId = clientId;
            Resource = resource;
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ResourceType
    {
        KeyVault,
        Storage,
        Sql,
        Custom
    }

    internal class MsiValidator
    {
        public MsiValidatorTestResult Result { get; set; }

        private readonly string _identityEndpoint;
        private readonly string _identitySecret;
        private static readonly HttpClient _client = new HttpClient();

        public MsiValidator()
        {
            _identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            _identitySecret = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            Result = new MsiValidatorTestResult();
        }

        private async Task<HttpResponseMessage> GetHttpResponseAsync(string url, Dictionary<string, string> headers)
        {
            _client.DefaultRequestHeaders.Clear();
            foreach (KeyValuePair<string, string> header in headers)
            {
                _client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }

            return await _client.GetAsync(url);
        }

        private async Task<ConnectivityResult> TestKeyVaultAsync(string endpoint)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "Authorization" , $"Bearer {Result.GetTokenTestResult.TokenInformation.AccessToken}"}
                };

            ConnectivityResult keyvaultConnectivityResult = new ConnectivityResult(ResourceType.KeyVault);

            endpoint = $"{endpoint}?api-version=2016-10-01";
            var response = await GetHttpResponseAsync(endpoint, headers);

            keyvaultConnectivityResult.IsSuccessful = response.IsSuccessStatusCode;
            keyvaultConnectivityResult.Response = await response.Content.ReadAsStringAsync();
            keyvaultConnectivityResult.Resource = "https://vault.azure.net";

            return keyvaultConnectivityResult;
        }

        private async Task<ConnectivityResult> TestStorageAsync(string endpoint)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "Authorization" , $"Bearer {Result.GetTokenTestResult.TokenInformation.AccessToken}"},
                    { "x-ms-version" , $"2017-11-09"}
                };

            HttpResponseMessage response = await GetHttpResponseAsync(endpoint, headers);

            ConnectivityResult storageConnectivityResult = new ConnectivityResult(ResourceType.Storage);
            storageConnectivityResult.IsSuccessful = response.IsSuccessStatusCode;
            storageConnectivityResult.Response = await response.Content.ReadAsStringAsync();
            storageConnectivityResult.Resource = "https://storage.azure.com";

            return storageConnectivityResult;
        }

        public bool IsEnabled()
        {
            // Logic : If any of these two env variables arent set, it means MSI is not enabled
            Result.MsiEnabled = !(string.IsNullOrEmpty(_identityEndpoint) ||
                                    string.IsNullOrWhiteSpace(_identityEndpoint) ||
                                    string.IsNullOrEmpty(_identitySecret) ||
                                    string.IsNullOrWhiteSpace(_identitySecret)
                                 );

            return Result.MsiEnabled;
        }

        public async Task<bool> GetTokenAsync(MsiValidatorInput input)
        {
            string url = $"{_identityEndpoint}?resource={input.Resource}&api-version=2019-08-01";
            if (!string.IsNullOrEmpty(input.ClientId))
            {
                url += $"&client_id={input.ClientId}";
            }

            Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "X-IDENTITY-HEADER" , _identitySecret}
                };

            HttpResponseMessage response = await GetHttpResponseAsync(url, headers);
            Result.GetTokenTestResult.IsSuccessful = response.IsSuccessStatusCode;
            if (response.IsSuccessStatusCode)
            {
                Result.GetTokenTestResult.TokenInformation = JsonConvert.DeserializeObject<TokenInformation>(await response.Content.ReadAsStringAsync());
            }
            else
            {
                Result.GetTokenTestResult.ErrorDetails = JsonConvert.DeserializeObject<AdalError>(await response.Content.ReadAsStringAsync());
            }

            return response.IsSuccessStatusCode;
        }

        public async Task TestConnectivityAsync(MsiValidatorInput input)
        {
            if (string.IsNullOrEmpty(input.Endpoint))
            {
                Result.ConnectivityResult.ResourceType = input.ResourceType;
                Result.ConnectivityResult.Resource = input.Resource;
                Result.ConnectivityResult.IsSuccessful = false;
                Result.ConnectivityResult.Response = $"The endpoint '{input.Endpoint}' is invalid.";

                return;
            }

            switch (input.ResourceType)
            {
                case ResourceType.KeyVault:
                    Result.ConnectivityResult = await TestKeyVaultAsync(input.Endpoint);
                    return;

                case ResourceType.Storage:
                    Result.ConnectivityResult = await TestStorageAsync(input.Endpoint);
                    return;
            }
        }
    }
}