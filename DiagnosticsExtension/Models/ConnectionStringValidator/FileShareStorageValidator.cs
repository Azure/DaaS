// -----------------------------------------------------------------------
// <copyright file="StorageValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
//using Microsoft.WindowsAzure.Storage;
//using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Files;
using Azure.Storage.Files.Shares;
using Microsoft.WindowsAzure.Storage;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class FileShareStorageValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.WindowsAzure.Storage";

        public ConnectionStringType Type => ConnectionStringType.FileShareStorageAccount;

        public async Task<bool> IsValidAsync(string connStr)
        {
            try
            {
                Uri objuri = new Uri(connStr);
                ShareServiceClient client = new ShareServiceClient(objuri);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public async Task<ConnectionStringValidationResult> ValidateAsync(string connStr, string clientId = null)
        {
            var response = new ConnectionStringValidationResult(Type);

            try
            {
                var result = await TestConnectionString(connStr, null, clientId);
                if (result.Succeeded)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Success;
                }
                else
                {
                    throw new Exception("Unexpected state reached: result.Succeeded == false is unexpected!");
                }
            }
            catch (Exception e)
            {
                if (e is MalformedConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e is EmptyConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                }
                else if (e.InnerException != null &&
                         e.InnerException.Message.Contains("The remote name could not be resolved"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                }
                else if (e is StorageException)
                {
                    if (((StorageException)e).RequestInformation.HttpStatusCode == 401)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                    }
                    else if (((StorageException)e).RequestInformation.HttpStatusCode == 403)
                    {
                        response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
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

        public async Task<TestConnectionData> TestConnectionString(string connectionString, string name, string clientId)
        {
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name,
                Succeeded = true
            };
            // CloudStorageAccount storageAccount;
            try
            {
                //storageAccount = CloudStorageAccount.Parse(connectionString);
            }
            catch (ArgumentNullException e)
            {
                throw new EmptyConnectionStringException(e.Message, e);
            }
            catch (Exception e)
            {
                throw new MalformedConnectionStringException(e.Message, e);
            }

            // CloudBlobClient client = storageAccount.CreateCloudBlobClient();
            // client.GetServiceProperties();
            // client.ListContainers();

            return data;
        }

        public async Task<ConnectionStringValidationResult> ValidateViaAppsettingAsync(string appsettingname, string entityName)
        {
            var response = new ConnectionStringValidationResult(Type);

            try
            {
                var result = await TestConnectionStringViaAppSetting(appsettingname, entityName);
                if (result.Succeeded)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.Success;
                }
                else
                {
                    throw new Exception("Unexpected state reached: result.Succeeded == false is unexpected!");
                }
            }
            catch (Exception e)
            {
                if (e is MalformedConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.MalformedConnectionString;
                }
                else if (e is EmptyConnectionStringException)
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.EmptyConnectionString;
                }
                else if (e.InnerException != null &&
                         e.InnerException.Message.Contains("The remote name could not be resolved"))
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.DnsLookupFailed;
                }
                //else if (e is StorageException)
                //{
                //    if (((StorageException)e).RequestInformation.HttpStatusCode == 401)
                //    {
                //        response.Status = ConnectionStringValidationResult.ResultStatus.AuthFailure;
                //    }
                //    else if (((StorageException)e).RequestInformation.HttpStatusCode == 403)
                //    {
                //        response.Status = ConnectionStringValidationResult.ResultStatus.Forbidden;
                //    }
                //}
                else
                {
                    response.Status = ConnectionStringValidationResult.ResultStatus.UnknownError;
                }
                response.Exception = e;
            }

            return response;
        }

        public async Task<TestConnectionData> TestConnectionStringViaAppSetting(string appSettingName, string entityName)
        {
            string value = "";
            var envDict = Environment.GetEnvironmentVariables();
            ShareServiceClient client = null;
            try
            {
                if (envDict.Contains(appSettingName))
                {
                    value = Environment.GetEnvironmentVariable(appSettingName);
                    client = new ShareServiceClient(value);
                }
                client.GetSharesAsync();
            }
            catch (ArgumentNullException e)
            {
                throw new EmptyConnectionStringException(e.Message, e);
            }
            catch (Exception e)
            {
                throw new MalformedConnectionStringException(e.Message, e);
            }
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = client.ToString(),
                Succeeded = true
            };
            return data;
        }
    }
}
