using Newtonsoft.Json;
using System.Collections.Generic;

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

    public class TestConnectivityResult
    {
        public string Resource { get; set; }

        public bool IsSuccessful { get; set; }

        public string Response { get; set; }
    }

    public class MsiValidatorTestResult
    {
        public readonly string MsiValidatorVersion;

        public bool MsiEnabled { get; set; }

        public GetTokenTestResult GetTokenTestResult { get; set; }

        public List<TestConnectivityResult> TestConnectivityResults { get; set; }

        public MsiValidatorTestResult()
        {
            MsiValidatorVersion = "v1.0.0.0";
            TestConnectivityResults = new List<TestConnectivityResult>();
            GetTokenTestResult = new GetTokenTestResult();
        }
    }
}