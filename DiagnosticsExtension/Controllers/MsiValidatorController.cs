using DiagnosticsExtension.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
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
        public class MsiValidatorInputs
        {
            public string ClientId { get; set; }

            public string Resource { get; set; }

            public string Endpoint { get; set; }

            public MsiValidatorInputs(string resource, string endpoint, string clientId)
            {
                this.Resource = resource;
                this.Endpoint = endpoint;
                this.ClientId = clientId;
            }
        }

        class MsiValidator
        {
            public MsiValidatorTestResult Result { get; set; }

            private readonly string identityEndpoint;
            private readonly string identitySecret;
            private static readonly HttpClient client = new HttpClient();
            private static Dictionary<string, Dictionary<string, string>> endpointConfig = new Dictionary<string, Dictionary<string, string>>()
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


            public MsiValidator()
            {
                identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
                identitySecret = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
                Result = new MsiValidatorTestResult();
            }

            private async Task<HttpResponseMessage> GetHttpResponseAsync(string url, Dictionary<string, string> headers)
            {
                client.DefaultRequestHeaders.Clear();
                foreach (KeyValuePair<string, string> header in headers)
                {
                    client.DefaultRequestHeaders.Add(header.Key, header.Value);
                }

                return await client.GetAsync(url);
            }


            private async Task<TestConnectivityResult> TestKeyVaultAsync(string endpoint)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "Authorization" , $"Bearer {Result.GetTokenTestResult.TokenInformation.AccessToken}"}
                };

                TestConnectivityResult testConnectivityResult = new TestConnectivityResult();

                endpoint = $"{endpoint}?api-version=2016-10-01";
                var response = await GetHttpResponseAsync(endpoint, headers);

                testConnectivityResult.IsSuccessful = response.IsSuccessStatusCode;
                testConnectivityResult.Response = await response.Content.ReadAsStringAsync();

                return testConnectivityResult;

            }

            private async Task<TestConnectivityResult> TestStorageAsync(string endpoint)
            {
                Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "Authorization" , $"Bearer {Result.GetTokenTestResult.TokenInformation.AccessToken}"},
                    { "x-ms-version" , $"2017-11-09"},

                };

                TestConnectivityResult testConnectivityResult = new TestConnectivityResult();

                HttpResponseMessage response = await GetHttpResponseAsync(endpoint, headers);

                testConnectivityResult.IsSuccessful = response.IsSuccessStatusCode;
                testConnectivityResult.Response = await response.Content.ReadAsStringAsync();

                return testConnectivityResult;

            }

            private async Task<TestConnectivityResult> TestSqlAsync(string connectionString)
            {
                TestConnectivityResult testConnectivityResult = new TestConnectivityResult();                                

                if (string.IsNullOrEmpty(connectionString))
                {
                    testConnectivityResult.IsSuccessful = false;
                    testConnectivityResult.Response = $"Could not find Connection String for SQL that is added to App Service. Navigate to Configuration Blade -> App Settings and add a new SQL connection String";

                    return testConnectivityResult;
                }

                SqlConnection conn = new SqlConnection(connectionString);
                string status;
                try
                {
                    conn.AccessToken = Result.GetTokenTestResult.TokenInformation.AccessToken;
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

                testConnectivityResult.IsSuccessful = status == "Success";
                testConnectivityResult.Response = status;


                return testConnectivityResult;
            }


            public bool IsEnabled()
            {
                // Logic : If any of these two env variables arent set, it means MSI is not enabled
                Result.MsiEnabled = !(string.IsNullOrEmpty(identityEndpoint) ||
                                            string.IsNullOrWhiteSpace(identityEndpoint) ||
                                            string.IsNullOrEmpty(identitySecret) ||
                                            string.IsNullOrWhiteSpace(identitySecret)
                                     );

                return Result.MsiEnabled;
            }

            public async Task<bool> GetTokenAsync(MsiValidatorInputs inputs)
            {

                string resourceUrl = endpointConfig[inputs.Resource]["url"];
                string url = $"{identityEndpoint}?resource={resourceUrl}&api-version=2019-08-01";
                if (!string.IsNullOrEmpty(inputs.ClientId))
                {
                    url += $"&client_id={inputs.ClientId}";
                }


                Dictionary<string, string> headers = new Dictionary<string, string>()
                {
                    { "X-IDENTITY-HEADER" , identitySecret}
                };

                var response = await GetHttpResponseAsync(url, headers);
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

            public async Task TestConnectivityAsync(MsiValidatorInputs inputs)
            {
                TestConnectivityResult testConnectivityResult = new TestConnectivityResult();

                // Default Case
                testConnectivityResult.IsSuccessful = false;
                testConnectivityResult.Response = $"The resource '{inputs.Resource}' specified is invalid. Supported value for resource are keyvault, storage, sql ";
                testConnectivityResult.Resource = inputs.Resource;

                if (inputs.Resource != "sql" && string.IsNullOrEmpty(inputs.Endpoint))
                {
                    testConnectivityResult.IsSuccessful = false;
                    testConnectivityResult.Response = $"The endpoint '{inputs.Endpoint}' is invalid.";
                    Result.TestConnectivityResults.Add(testConnectivityResult);

                    return;

                }

                switch (inputs.Resource)
                {
                    case "keyvault":
                        testConnectivityResult =  await TestKeyVaultAsync(inputs.Endpoint);                        
                        break;

                    case "storage":
                        testConnectivityResult = await TestStorageAsync(inputs.Endpoint);
                        break;

                    case "sql":
                        foreach (System.Collections.DictionaryEntry envVar in Environment.GetEnvironmentVariables())
                        {
                            if (envVar.Key.ToString().StartsWith("SQLCONNSTR_"))
                            {
                                string connectionString = envVar.Value.ToString();
                                Result.TestConnectivityResults.Add(await TestSqlAsync(connectionString));                           
                            }
                        }
                        break;
                }

                if(inputs.Resource != "sql")
                {
                    testConnectivityResult.Resource = inputs.Resource;
                    Result.TestConnectivityResults.Add(testConnectivityResult);
                }
            }
        }

        public async Task<HttpResponseMessage> Get(string resource, string endpoint = null, string clientId = null)
        {
            try
            {
                MsiValidator msi = new MsiValidator();
                MsiValidatorInputs testInputs = new MsiValidatorInputs(resource, endpoint, clientId);

                if (msi.IsEnabled())
                {
                    List<string> validResources = new List<string>()
                    {
                         "keyvault", "storage", "sql"
                    };
                    if (validResources.Contains(testInputs.Resource))
                    {
                        // Step 1 : Check if we are able to get an access token 
                        bool connectivityWithAzureActiveDirectory = await msi.GetTokenAsync(testInputs);

                        // Step 2 : Test Connectivity to endpoint
                        if (connectivityWithAzureActiveDirectory)
                            await msi.TestConnectivityAsync(testInputs);
                    }
                    else
                    {
                        return Request.CreateResponse(HttpStatusCode.OK, $"MSI Validator is not available for the resource : {testInputs.Resource}");
                    }
                }
                               

                return Request.CreateResponse(HttpStatusCode.OK, msi.Result);
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Encountered exception while checking appinfo", ex);
                return Request.CreateErrorResponse(HttpStatusCode.OK, ex.Message);
            }
        }

    }
}