using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace DiagnosticsExtension.Models
{
    public class AccessTokenInformation
    {
        [JsonProperty("access_token")]
        public string accessToken;

        [JsonProperty("expires_on")]
        public string expiresOn;

        [JsonProperty("resource")]
        public string resource;

        [JsonProperty("token_type")]
        public string tokenType;

        [JsonProperty("client_id")]
        public string clientId;

    }

    public class GetTokenTestFailureException
    {
        [JsonProperty("ExceptionMessage")]
        public string exceptionMessage;

        [JsonProperty("ErrorCode")]
        public string errorCode;

        [JsonProperty("ServiceErrorCodes")]
        public List<string> ServiceErrorCodes;

        [JsonProperty("InnerException")]
        public string innerException;

        [JsonProperty("StatusCode")]
        public int statusCode;

        [JsonProperty("Message")]
        public string message;

        [JsonProperty("CorrelationId")]
        public string correlationId;
    }

    public class TestConnectivityResults
    {
        public bool isSuccessful;

        public string response;

    }

    public class MsiValidatorTestResults
    {
        public readonly string msiValidatorVersion;

        public bool msiEnabled;

        public bool getAccessTokenTestResult;
                
        public AccessTokenInformation accessTokenInformation;

        public GetTokenTestFailureException getTokenException;

        public TestConnectivityResults testConnectivityResults;

        public MsiValidatorTestResults()
        {
            msiValidatorVersion = "v1.0.0.0";
        }

    }
}