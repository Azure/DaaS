using Newtonsoft.Json;
using System.Collections.Generic;

namespace DiagnosticsExtension.Models
{
    public class AccessTokenInformation
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

    public class GetTokenTestFailureException
    {
        [JsonProperty("ExceptionMessage")]
        public string ExceptionMessage { get; set; }

        [JsonProperty("ErrorCode")]
        public string ErrorCode { get; set; }

        [JsonProperty("ServiceErrorCodes")]
        public List<string> ServiceErrorCodes { get; set; }

        [JsonProperty("InnerException")]
        public string InnerException { get; set; }

        [JsonProperty("StatusCode")]
        public int StatusCode { get; set; }

        [JsonProperty("Message")]
        public string Message { get; set; }

        [JsonProperty("CorrelationId")]
        public string CorrelationId { get; set; }
    }

    public class TestConnectivityResult
    {
        public bool IsSuccessful { get; set; }

        public string Response { get; set; }

    }

    public class MsiValidatorTestResults
    {
        public readonly string MsiValidatorVersion;

        public bool MsiEnabled { get; set; }

        public bool GetAccessTokenTestResult { get; set; }

        public AccessTokenInformation AccessTokenInformation { get; set; }

        public GetTokenTestFailureException GetTokenException { get; set; }

        public TestConnectivityResult TestConnectivityResult { get; set; }

        public MsiValidatorTestResults()
        {
            MsiValidatorVersion = "v1.0.0.0";
        }

    }
}