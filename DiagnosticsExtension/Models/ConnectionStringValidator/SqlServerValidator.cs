using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
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
            try
            {
                var builder = new SqlConnectionStringBuilder(connStr);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public async Task<ConnectionStringValidationResult> Validate(string connStr, string clientId = null)
        {
            var response = new ConnectionStringValidationResult(Type);

            try
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(connStr);
                }
                catch (Exception e)
                {
                    throw new MalformedConnectionStringException(e.Message ,e);
                }

                var result = await TestSqlServerConnectionString(connStr, null, clientId);
                if (result.Succeeded)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Success;
                }
                else
                {
                    if (result.MsiAdalError != null)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.MsiFailure;
                        var e = new Exception(result.MsiAdalError.Message);
                        e.Data["AdalError"] = result.MsiAdalError;
                        response.Exception = e;
                    }
                    else
                    {
                        throw new Exception("Unexpected state reached: result.Succeeded == false &&  result.MsiAdalError == null is unexpected!");
                    }
                }
            }
            catch (Exception e)
            {
                if (e is MalformedConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e is InvalidOperationException && e.Message.StartsWith("Cannot set the AccessToken property"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e is SqlException)
                {
                    if (e.Message.Contains("The server was not found or was not accessible"))
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.EndpointNotReachable;
                    }
                    else if (e.Message.StartsWith("A network-related or instance-specific error"))
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.ConnectionFailure;
                    }
                    else if (e.Message.ToLower().Contains("login failed"))
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                    }
                    else if (e.Message.Contains("not allow"))
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                    }
                    else
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                    }
                }
                else
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                }
                response.Exception = e;
            }

            return response;
        }

        public async Task<TestConnectionData> TestSqlServerConnectionString(string connectionString, string name, string clientId)
        {
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name
            };
            data.Succeeded = false;

            using (SqlConnection conn = new SqlConnection())
            {
                conn.ConnectionString = connectionString;
                SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(conn.ConnectionString);
                string userId = builder.UserID;
                string password = builder.Password;
                bool hasConnectivityWithAzureAd = true;

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
                {
                    // when we have access token from azure ad
                    MsiValidator msi = new MsiValidator();

                    data.IsMsiEnabled = msi.IsEnabled();

                    if (data.IsMsiEnabled)
                    {
                        MsiValidatorInput input = new MsiValidatorInput(ResourceType.Sql, clientId);
                        hasConnectivityWithAzureAd = await msi.GetTokenAsync(input);

                        if (hasConnectivityWithAzureAd)
                        {
                            conn.AccessToken = msi.Result.GetTokenTestResult.TokenInformation.AccessToken;
                            await conn.OpenAsync();
                            data.Succeeded = true;
                        }
                        else
                        {
                            data.MsiAdalError = msi.Result.GetTokenTestResult.ErrorDetails;
                        }
                    }
                }
                else
                {
                    // when connectionString has credentials
                    await conn.OpenAsync();
                    data.Succeeded = true;
                }
            }

            return data;
        }
    }
}