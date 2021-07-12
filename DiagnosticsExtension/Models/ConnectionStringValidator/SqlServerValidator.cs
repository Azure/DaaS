using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class SqlServerValidator : IConnectionStringValidator
    {
        public string ProviderName => "System.Data.SqlClient";

        public ConnectionStringType Type => ConnectionStringType.SqlServer;

        public bool IsValid(string connStr)
        {
            throw new NotImplementedException();
        }

        async public Task<ConnectionStringValidationResult> Test(string connStr, string clientId = null)
        {
            var response = new ConnectionStringValidationResult();
            using (SqlConnection conn = new SqlConnection())
            {
                try
                {
                    conn.ConnectionString = connStr;
                    SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(conn.ConnectionString);
                    string userId = builder.UserID;
                    string password = builder.Password;

                    if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
                    {
                        MsiValidator msi = new MsiValidator();

                        if (msi.IsEnabled())
                        {
                            var input = new MsiValidatorInput(ResourceType.Sql, clientId);
                            bool hasConnectivityWithAzureAd = await msi.GetTokenAsync(input);

                            if (hasConnectivityWithAzureAd)
                            {
                                conn.AccessToken = msi.Result.GetTokenTestResult.TokenInformation.AccessToken;
                            }
                            else
                            {
                                var adalError = msi.Result.GetTokenTestResult.ErrorDetails;
                                var e = new Exception(adalError.Message);
                                e.Data["AdalError"] = adalError;
                                response.Status = ConnectionStringValidationResult.ResultStatus.MsiFailed;
                                response.Exception = e;
                                return response;
                            }
                        }
                    }


                    await conn.OpenAsync();
                    response.Status = ConnectionStringValidationResult.ResultStatus.Succeeded;
                }
                catch (Exception e)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                    response.Exception = e;
                }

                return response;
            }
        }
    }
}