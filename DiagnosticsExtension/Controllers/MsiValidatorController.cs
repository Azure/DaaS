using DiagnosticsExtension.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    [RoutePrefix("api/msivalidator")]
    public class MsiValidatorController : ApiController
    {
        public class InputsForMsiValidator
        {
            public string clientId { get; set; }

            public string resource { get; set; }

            public string endpoint { get; set; }

            public InputsForMsiValidator(string resource, string endpoint, string clientId)
            {
                this.resource = resource;
                this.endpoint = endpoint;
                this.clientId = clientId;
            }
        }

        class MSIValidator
        {
            private MsiValidatorTestResults result = new MsiValidatorTestResults();
            private readonly string _identityEndpoint;
            private readonly string _identitySecret;
            private static readonly HttpClient _client = new HttpClient();
            private static Dictionary<string, Dictionary<string, string>> _endpointConfig = new Dictionary<string, Dictionary<string, string>>()
            {
                {
                    "keyvault", new Dictionary<string,string>
                    {
                        { "url", "https://vault.azure.net" },
                        { "apiVersion", "2016-10-01" }
                    }
                },
                {  "storage" , new Dictionary<string,string>
                    {
                        { "url" , "https://storage.azure.com" },
                        { "apiVersion" , "2018-02-01"  }
                    }
                },
                {  "sql" , new Dictionary<string,string>
                    {
                        { "url" , "https://database.windows.net" },
                        { "apiVersion" , ""  }
                    }
                },
                {  "default" , new Dictionary<string,string>
                    {
                        { "url" , "https://management.azure.com" },
                        { "apiVersion" , ""  }
                    }
                }
            };


            public MSIValidator()
            {
                _identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
                _identitySecret = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            }

            private async Task<HttpResponseMessage> GetHttpResponse(string url, Dictionary<string, string> headers)
            {
                _client.DefaultRequestHeaders.Clear();
                foreach (KeyValuePair<string, string> header in headers)
                {
                    _client.DefaultRequestHeaders.Add(header.Key, header.Value);
                }

                return await _client.GetAsync(url);
            }
                       

            private async Task KeyVaultTest(string endpoint)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "Authorization" , $"Bearer {result.accessTokenInformation.accessToken}"}
                };

                TestConnectivityResults testConnectivityResult = new TestConnectivityResults();

                endpoint = $"{endpoint}?api-version=2016-10-01";
                var response = await GetHttpResponse(endpoint, headers);

                testConnectivityResult.isSuccessful = response.IsSuccessStatusCode;
                testConnectivityResult.response = await response.Content.ReadAsStringAsync();

                result.testConnectivityResults = testConnectivityResult;

            }

            private async Task StorageTest(string endpoint)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "Authorization" , $"Bearer {result.accessTokenInformation.accessToken}"},
                    { "x-ms-version" , $"2017-11-09"},

                };

                TestConnectivityResults testConnectivityResult = new TestConnectivityResults();

                var response = await GetHttpResponse(endpoint, headers);

                testConnectivityResult.isSuccessful = response.IsSuccessStatusCode;
                testConnectivityResult.response = await response.Content.ReadAsStringAsync();

                result.testConnectivityResults = testConnectivityResult;

            }

            private async Task SqlTest()
            {
                string connectionString = "";
                result.testConnectivityResults = new TestConnectivityResults();

                foreach (System.Collections.DictionaryEntry envVar in Environment.GetEnvironmentVariables())
                {
                    if(envVar.Key.ToString().StartsWith("SQLCONNSTR_"))
                    {
                        connectionString = envVar.Value.ToString();
                        break;
                    }
                }

                if(string.IsNullOrEmpty(connectionString ))
                {
                    result.testConnectivityResults.isSuccessful = false;
                    result.testConnectivityResults.response = $"Could not find Connection String for SQL that is added to App Service. Navigate to Configuration Blade -> App Settings and add a new SQL connection String";
                                        
                    return;
                }

                SqlConnection conn = new SqlConnection(connectionString);
                string status;
                try
                {
                    conn.AccessToken = result.accessTokenInformation.accessToken;
                    await conn.OpenAsync();
                    status = "Success";
                }
                catch (Exception ex)
                {
                    status = $"Unable to connect to SQL. Exception : {ex.Message}";
                }
                finally
                {
                    conn.Close();
                }

                

                result.testConnectivityResults.isSuccessful = status == "Success";
                result.testConnectivityResults.response = status;


            }

            

            public MsiValidatorTestResults GetResult()
            {
                return result;
            }            

            public bool IsEnabled()
            {
                // Logic : If any of these two env variables arent set, it means MSI is not enabled

            result.msiEnabled = !(      string.IsNullOrEmpty(_identityEndpoint) ||
                                        string.IsNullOrWhiteSpace(_identityEndpoint) ||
                                        string.IsNullOrEmpty(_identitySecret) ||
                                        string.IsNullOrWhiteSpace(_identitySecret)
                                 );

                return result.msiEnabled;                
            }
            
            public async Task<bool> GetToken(InputsForMsiValidator inputs)
            {

                string resourceUrl = _endpointConfig[inputs.resource]["url"];
                string url = $"{_identityEndpoint}?resource={resourceUrl}&api-version=2019-08-01"; 
                if (! string.IsNullOrEmpty(inputs.clientId))
                {
                    url += $"&client_id={inputs.clientId}";
                }
                

                Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "X-IDENTITY-HEADER" , _identitySecret}
                };

                var response = await GetHttpResponse(url,headers);
                result.getAccessTokenTestResult = response.IsSuccessStatusCode;
                if (response.IsSuccessStatusCode)
                {
                    result.accessTokenInformation = JsonConvert.DeserializeObject<AccessTokenInformation>(await response.Content.ReadAsStringAsync());                   
                }
                else
                {
                    result.getTokenException = JsonConvert.DeserializeObject<GetTokenTestFailureException>(await response.Content.ReadAsStringAsync());
                }

                return response.IsSuccessStatusCode;
            }     
               
            public async Task TestConnectivity(InputsForMsiValidator inputs)
            {

                if (inputs.resource!= "sql" && string.IsNullOrEmpty(inputs.endpoint))
                {
                    result.testConnectivityResults = new TestConnectivityResults();
                    result.testConnectivityResults.isSuccessful = false;
                    result.testConnectivityResults.response = $"The endpoint '{inputs.endpoint}' is invalid.";
                    return;
                }
                    

                switch(inputs.resource)
                {
                    case "keyvault":
                         await KeyVaultTest(inputs.endpoint);
                        break;

                    case "storage":
                        await StorageTest(inputs.endpoint);
                        break;

                    case "sql":
                        await SqlTest();
                        break;
                    default:
                        result.testConnectivityResults.isSuccessful = false;
                        result.testConnectivityResults.response = $"The resource '{inputs.resource}' specified is invalid. Supported value for resource are keyvault, storage, sql ";
                        break;

                }
            }



        }

        

        public async Task<HttpResponseMessage> Get(string resource, string endpoint=null, string clientId = null)
        {
            try
            {
                MSIValidator msi = new MSIValidator();
                InputsForMsiValidator testInputs = new InputsForMsiValidator(resource, endpoint, clientId);
                

                if (msi.IsEnabled())
                {
                    List<string> validResources = new List<string>()
                    {
                         "keyvault", "storage", "sql"
                    };
                    if (validResources.Contains(testInputs.resource))
                    {
                        // Step 1 : Check if we are able to get an access token 
                        bool connectivityWithAzureActiveDirectory = await msi.GetToken(testInputs);

                        // Step 2 : Test Connectivity to endpoint
                        if (connectivityWithAzureActiveDirectory)
                            await msi.TestConnectivity(testInputs);
                    }
                    else
                    {
                        return Request.CreateResponse(HttpStatusCode.OK, $"MSI Validator is not available for the resource : {testInputs.resource}");
                    }

                    

                }                
                                
                return Request.CreateResponse(HttpStatusCode.OK, msi.GetResult());
            }
            catch (Exception ex)
            {
              DaaS.Logger.LogErrorEvent("Encountered exception while checking appinfo", ex);
              return Request.CreateErrorResponse(HttpStatusCode.OK, ex.Message);
            }
        }

    }
}