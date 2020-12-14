//-----------------------------------------------------------------------
// <copyright file="DatabaseTestController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DiagnosticsExtension.Models;
using Microsoft.WindowsAzure.Storage;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Npgsql;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Data.Entity.Core.EntityClient;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public enum DatabaseType
    {
        SqlDatabase,
        SqlServer,
        MySql,
        Custom,
        Dynamic,
        NotSupported,
        PostgreSql,
        RedisCache
    }

    public class DatabaseConnection
    {
        public string Name;
        public string ConnectionString;
        public string ProviderName;
        public bool Succeeded;
        public Exception ExceptionDetails;
        public DatabaseType DatabaseType;
        public string Instance;
        public bool DummyValueExistsInWebConfig;
        public string FilePath;
        public int LineNumber;
        public bool IsEnvironmentVariable;
        public bool IsEntityFramework;
        public bool IsAzureStorage;
        public bool IsCustomDriver;
        public bool IsMsiEnabled;
        public AdalError MsiAdalError;
        //public string MaskedConnectionString;
    }

    public class DatabaseConnectionStats
    {
        public string StatsType;
        public string SiteName;
        public int InConfig;
        public int EnvironmentVariable;
        public int DummyValueExistsInConfig;
        public int SqlServer;
        public int MySql;
        public int Custom;
        public int SqlDatabase;
        public int EntityFramework;
        public int Succeeded;
        public int Total;
        public int UnRecognizedConnectionStrings;
        public List<string> ProviderNames = new List<string>();
        public int CustomDriver;
        public int AzureStorage;
    }

    public class TestConnectionData
    {
        public bool Succeeded;
        public Exception ExceptionDetails;
        public bool IsEntityFramework;
        public bool IsAzureStorage;
        public bool IsCustomDriver;
        public string Name;
        public bool IsMsiEnabled;
        public AdalError MsiAdalError;
        public string ConnectionString { get; internal set; }
    }

    public class DatabaseTestResponse
    {
        public IEnumerable<DatabaseConnection> Connections { get; set; }
        public string ConfigException { get; set; }
    }

    [RoutePrefix("api/databasetest")]
    public class DatabaseTestController : ApiController
    {
        private const string AppSettingPrefix = "APPSETTING_";
        private const string SqlServerPrefix = "SQLCONNSTR_";
        private const string MySqlServerPrefix = "MYSQLCONNSTR_";
        private const string SqlAzureServerPrefix = "SQLAZURECONNSTR_";
        private const string PostgreSqlPrefix = "POSTGRESQLCONNSTR_";
        private const string RedisCachePrefix = "REDISCACHECONNSTR_";

        ////////////////////////////////////////////////////////////////////////////////////////////////
        // Still need to implement these ones
        private const string NotificationHubPrefix = "NOTIFICATIONHUBCONNSTR_";

        private const string ServiceBusPrefix = "SERVICEBUSCONNSTR_";
        private const string EventHubPrefix = "EVENTHUBCONNSTR_";
        private const string ApiHubPrefix = "APIHUBCONNSTR_";
        private const string DocDBPrefix = "DOCDBCONNSTR_";
        ////////////////////////////////////////////////////////////////////////////////////////////////

        private const string CustomPrefix = "CUSTOMCONNSTR_";

        // GET api/databasetest
        public async Task<HttpResponseMessage> Get(string clientId = null) // clientId used for Used Assigned Managed Identity
        {
            try
            {
                var response = await GetConnectionsResponse(clientId);
                return Request.CreateResponse(HttpStatusCode.OK, response.Connections);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        // GET api/databasetest/v2
        [Route("v2")]
        public async Task<HttpResponseMessage> GetV2(string clientId = null)
        {
            try
            {
                var response = await GetConnectionsResponse(clientId);
                return Request.CreateResponse(HttpStatusCode.OK, response);
            }
            catch (Exception ex)
            {
                return Request.CreateErrorResponse(HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private async Task<DatabaseTestResponse> GetConnectionsResponse(string clientId)
        {
            DatabaseTestResponse response = new DatabaseTestResponse();
            List<DatabaseConnection> connections = new List<DatabaseConnection>();
            foreach (System.Collections.DictionaryEntry envVar in Environment.GetEnvironmentVariables())
            {
                var name = (string)envVar.Key;
                var val = (string)envVar.Value;

                if (envVar.Key.ToString().Contains("CONNSTR_"))
                {
                    var connection = new DatabaseConnection();
                    string connectionString = envVar.Value.ToString();

                    if (name.StartsWith(SqlAzureServerPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(SqlAzureServerPrefix.Length);
                        connection.DatabaseType = DatabaseType.SqlDatabase;
                        connection.ProviderName = "System.Data.SqlClient";
                    }
                    else if (name.StartsWith(SqlServerPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(SqlServerPrefix.Length);
                        connection.DatabaseType = DatabaseType.SqlServer;
                        connection.ProviderName = "System.Data.SqlClient";
                    }
                    else if (name.StartsWith(MySqlServerPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(MySqlServerPrefix.Length);
                        connection.DatabaseType = DatabaseType.MySql;
                        connection.ProviderName = "MySql.Data.MySqlClient";
                    }
                    else if (name.StartsWith(PostgreSqlPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(PostgreSqlPrefix.Length);
                        connection.DatabaseType = DatabaseType.PostgreSql;
                        connection.ProviderName = "Npgsql - .NET Access to PostgreSQL (Version - 4.0.4)";
                    }
                    else if (name.StartsWith(RedisCachePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(RedisCachePrefix.Length);
                        connection.DatabaseType = DatabaseType.RedisCache;
                        connection.ProviderName = "StackExchange.Redis (Version - 1.2.6)";
                    }
                    else if (name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(CustomPrefix.Length);
                        connection.DatabaseType = DatabaseType.Custom;
                        connection.ProviderName = "Custom";
                    }
                    else
                    {
                        // So this is a new kind of Database Type that we don't understand
                        string connectionStringType = GetConnectionStringType(name);
                        if (!string.IsNullOrWhiteSpace(connectionStringType))
                        {
                            connection.ProviderName = connectionStringType;
                        }
                        else
                        {
                            connection.ProviderName = "NotSupported";
                        }
                        connection.DatabaseType = DatabaseType.NotSupported;
                    }

                    connection.Name = name;
                    connection.IsEnvironmentVariable = true;
                    connection.ConnectionString = connectionString;
                    //connection.MaskedConnectionString = MaskPasswordFromConnectionString(connectionString);
                    connection.Instance = Environment.MachineName;
                    connections.Add(connection);
                }
            }

            try
            {
                var connectionsWebConfig = CheckConnectionstringsInSiteWebConfig();
                foreach (var c in connectionsWebConfig)
                {
                    var connMatching = connections.FirstOrDefault(x => x.Name == c.Name);
                    if (connMatching != null)
                    {
                        connMatching.DummyValueExistsInWebConfig = true;
                    }
                    else
                    {
                        connections.Add(c);
                    }
                }
            }
            catch (Exception ex)
            {
                response.ConfigException = "Failed while reading connections from web.config file. " + ex.Message;
            }

            var tasks = new List<Task<TestConnectionData>>();
            foreach (var c in connections)
            {
                if (c.IsEnvironmentVariable)
                {
                    tasks.Add(TestConnectionAsync(c.ConnectionString, c.Name, c.DatabaseType, clientId));
                }
                else
                {
                    tasks.Add(TestConnectionAsync(c.ConnectionString, c.ProviderName, c.Name));
                }
            }

            foreach (var task in await Task.WhenAll(tasks))
            {
                var c = connections.Find(x => x.ConnectionString == task.ConnectionString && x.Name == task.Name);
                if (c != null)
                {
                    c.Succeeded = task.Succeeded;
                    c.ExceptionDetails = task.ExceptionDetails;
                    c.IsAzureStorage = task.IsAzureStorage;
                    c.IsCustomDriver = task.IsCustomDriver;
                    c.IsEntityFramework = task.IsEntityFramework;
                    c.IsMsiEnabled = task.IsMsiEnabled;
                    c.MsiAdalError = task.MsiAdalError;
                }
            }

            LogConnectionStatsToKusto(connections);
            response.Connections = connections;
            return response;
        }

        private void LogConnectionStatsToKusto(List<DatabaseConnection> connections)
        {
            DatabaseConnectionStats stats = new DatabaseConnectionStats
            {
                StatsType = "ConnectionStringChecker",
                SiteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"),
                Total = connections.Count
            };

            stats.Succeeded = connections.Count(x => x.Succeeded);
            stats.DummyValueExistsInConfig = connections.Count(x => x.DummyValueExistsInWebConfig);
            stats.EnvironmentVariable = connections.Count(x => x.IsEnvironmentVariable);
            stats.InConfig = connections.Count(x => !string.IsNullOrWhiteSpace(x.FilePath));
            stats.Custom = connections.Count(x => x.DatabaseType == DatabaseType.Custom);
            stats.SqlServer = connections.Count(x => x.DatabaseType == DatabaseType.SqlServer);
            stats.SqlDatabase = connections.Count(x => x.DatabaseType == DatabaseType.SqlDatabase);
            stats.MySql = connections.Count(x => x.DatabaseType == DatabaseType.MySql);
            stats.UnRecognizedConnectionStrings = connections.Count(x => x.ExceptionDetails != null && (x.ExceptionDetails.Message == "Failed to determine the kind of connection string" || x.ExceptionDetails.Message.Contains("is not yet supported by this tool")));
            stats.EntityFramework = connections.Count(x => x.IsEntityFramework);
            stats.AzureStorage = connections.Count(x => x.IsAzureStorage);
            stats.CustomDriver = connections.Count(x => x.IsCustomDriver);
            stats.ProviderNames = connections.Select(x => x.ProviderName).ToList();

            DaaS.Logger.LogVerboseEvent(JsonConvert.SerializeObject(stats));
        }

        private List<DatabaseConnection> CheckConnectionstringsInSiteWebConfig()
        {
            List<DatabaseConnection> connections = new List<DatabaseConnection>();
            var webConfigFile = Path.Combine(DaaS.EnvironmentVariables.WebConfigFilePath);

            if (File.Exists(webConfigFile))
            {
                ExeConfigurationFileMap fileMap = new ExeConfigurationFileMap
                {
                    ExeConfigFilename = webConfigFile
                };

                Configuration config = ConfigurationManager.OpenMappedExeConfiguration(fileMap, ConfigurationUserLevel.None);
                ConnectionStringsSection csSection = (ConnectionStringsSection)config.GetSection("connectionStrings");
                var defaultProviderName = "System.Data.SqlClient";

                foreach (ConnectionStringSettings c in csSection.ConnectionStrings)
                {
                    if (c.ElementInformation != null)
                    {
                        DatabaseConnection connection = new DatabaseConnection
                        {
                            ConnectionString = c.ConnectionString,
                            //connection.MaskedConnectionString = MaskPasswordFromConnectionString(c.ConnectionString);
                            Instance = Environment.MachineName,
                            ProviderName = (string.IsNullOrWhiteSpace(c.ProviderName)) ? defaultProviderName : c.ProviderName,
                            Name = c.Name
                        };
                        if (c.ElementInformation != null)
                        {
                            if (c.ElementInformation.Source == null)
                            {
                                if (c.ElementInformation.Properties["name"] != null)
                                {
                                    if (c.ElementInformation.Properties["name"].Source != null)
                                    {
                                        connection.FilePath = c.ElementInformation.Properties["name"].Source;
                                        connection.LineNumber = c.ElementInformation.Properties["name"].LineNumber;
                                    }
                                }
                            }
                            else
                            {
                                connection.FilePath = c.ElementInformation.Source;
                                connection.LineNumber = c.ElementInformation.LineNumber;
                            }
                            //TODO: Only for debug purposes - connection.elementInfo = c.ElementInformation;
                        }
                        if (!connection.FilePath.EndsWith("machine.config", StringComparison.OrdinalIgnoreCase))
                        {
                            connections.Add(connection);
                        }
                    }
                }
            }
            return connections;
        }

        private async Task<TestConnectionData> TestConnectionAsync(string connectionString, string providerName, string name)
        {
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name
            };
            if (providerName.Equals("System.Data.EntityClient", StringComparison.OrdinalIgnoreCase) || connectionString.StartsWith("metadata=res://", StringComparison.OrdinalIgnoreCase))
            {
                data.IsEntityFramework = true;
                var ec = new EntityConnectionStringBuilder(connectionString);
                connectionString = ec.ProviderConnectionString;
                providerName = ec.Provider;
            }
            try
            {
                using (var connection = CreateDbConnection(providerName, connectionString))
                {
                    if (connection != null)
                    {
                        await connection.OpenAsync();
                        data.Succeeded = true;
                    }
                }
            }
            catch (Exception exception)
            {
                data.ExceptionDetails = exception;
            }
            return data;
        }

        private async Task<TestConnectionData> TestConnectionAsync(string connectionString, string name, DatabaseType databaseType = DatabaseType.Dynamic, string clientId = null)
        {
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name
            };

            try
            {
                if (databaseType == DatabaseType.SqlDatabase || databaseType == DatabaseType.SqlServer)
                {
                    using (SqlConnection conn = new SqlConnection())
                    {
                        conn.ConnectionString = connectionString;
                        SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(conn.ConnectionString);
                        string userId = builder.UserID;
                        string password = builder.Password;
                        bool hasConnectivityWithAzureAd = true;

                        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(password))
                        {
                            MsiValidator msi = new MsiValidator();

                            data.IsMsiEnabled = msi.IsEnabled();

                            if (data.IsMsiEnabled)
                            {
                                MsiValidatorInput input = new MsiValidatorInput(ResourceType.Sql, clientId);
                                hasConnectivityWithAzureAd = await msi.GetTokenAsync(input);

                                if (hasConnectivityWithAzureAd)
                                    conn.AccessToken = msi.Result.GetTokenTestResult.TokenInformation.AccessToken;
                                else
                                    data.MsiAdalError = msi.Result.GetTokenTestResult.ErrorDetails;
                            }
                        }

                        if (hasConnectivityWithAzureAd) // when connectionString has credentials or when we have access token from azure ad
                        {
                            await conn.OpenAsync();
                            data.Succeeded = true;
                        }
                        else
                        {
                            data.Succeeded = false;
                        }
                    }
                }
                else if (databaseType == DatabaseType.MySql)
                {
                    using (MySqlConnection conn = new MySqlConnection())
                    {
                        conn.ConnectionString = connectionString;
                        await conn.OpenAsync();
                        data.Succeeded = true;
                    }
                }
                else if (databaseType == DatabaseType.PostgreSql)
                {
                    using (NpgsqlConnection conn = new NpgsqlConnection())
                    {
                        conn.ConnectionString = connectionString;
                        await conn.OpenAsync();
                        data.Succeeded = true;
                    }
                }
                else if (databaseType == DatabaseType.RedisCache)
                {
                    using (var muxer = await ConnectionMultiplexer.ConnectAsync(connectionString))
                    {
                        data.Succeeded = true;
                    }
                }
                else if (databaseType == DatabaseType.Dynamic)
                {
                    using (SqlConnection conn = new SqlConnection())
                    {
                        conn.ConnectionString = connectionString;
                        await conn.OpenAsync();
                        data.Succeeded = true;
                    }
                }
                else if (databaseType == DatabaseType.NotSupported)
                {
                    throw new Exception("This type of connection string is not yet supported by this tool");
                }
                else if (databaseType == DatabaseType.Custom)
                {
                    if (connectionString.StartsWith("metadata=res://", StringComparison.OrdinalIgnoreCase))
                    {
                        data.IsEntityFramework = true;
                        var ec = new EntityConnectionStringBuilder(connectionString);
                        using (var connection = CreateDbConnection(ec.Provider, ec.ProviderConnectionString))
                        {
                            if (connection != null)
                            {
                                await connection.OpenAsync();
                                data.Succeeded = true;
                            }
                        }
                    }
                    else if (connectionString.IndexOf("Driver=", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        data.IsCustomDriver = true;
                        using (OdbcConnection conn = new OdbcConnection())
                        {
                            conn.ConnectionString = connectionString;
                            await conn.OpenAsync();
                            data.Succeeded = true;
                        }
                    }
                    else if (CloudStorageAccount.TryParse(connectionString, out CloudStorageAccount csa))
                    {
                        data.IsAzureStorage = true;
                        var cloudTableClient = csa.CreateCloudTableClient();
                        var tableNames = await cloudTableClient.ListTablesSegmentedAsync(null);
                        data.Succeeded = true;
                    }
                    else
                    {
                        throw new Exception("Failed to determine the kind of connection string");
                    }
                }
            }
            catch (Exception exception)
            {
                data.ExceptionDetails = exception;
            }
            return data;
        }

        private string GetConnectionStringType(string name)
        {
            string connType = string.Empty;
            if (name.Contains("_"))
            {
                var nameParts = name.Split('_');
                connType = nameParts[0];

                if (connType.EndsWith("CONNSTR", StringComparison.OrdinalIgnoreCase))
                {
                    connType = connType.ToUpper().Replace("CONNSTR", "");
                }
            }

            return connType;
        }

        private DbConnection CreateDbConnection(string providerName, string connectionString)
        {
            DbConnection connection = null;
            if (connectionString != null)
            {
                DbProviderFactory factory = DbProviderFactories.GetFactory(providerName);
                connection = factory.CreateConnection();
                connection.ConnectionString = connectionString;
            }
            return connection;
        }
    }
}