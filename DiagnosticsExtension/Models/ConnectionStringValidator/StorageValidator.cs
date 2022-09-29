// -----------------------------------------------------------------------
// <copyright file="StorageValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DiagnosticsExtension.Controllers;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public class StorageValidator : IConnectionStringValidator
    {
        public string ProviderName => "Microsoft.WindowsAzure.Storage";

        public ConnectionStringType Type => ConnectionStringType.StorageAccount;

        public Task<bool> IsValidAsync(string connStr)
        {
            try
            {
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connStr);
            }
            catch (Exception)
            {
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
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

        public Task<TestConnectionData> TestConnectionString(string connectionString, string name, string clientId)
        {
            TestConnectionData data = new TestConnectionData
            {
                ConnectionString = connectionString,
                Name = name,
                Succeeded = true
            };
            CloudStorageAccount storageAccount;
            try
            {
                storageAccount = CloudStorageAccount.Parse(connectionString);
            }
            catch (ArgumentNullException e)
            {
                throw new EmptyConnectionStringException(e.Message, e);
            }
            catch (Exception e)
            {
                throw new MalformedConnectionStringException(e.Message, e);
            }

            CloudBlobClient client = storageAccount.CreateCloudBlobClient();
            client.GetServiceProperties();
            IEnumerable<CloudBlobContainer> containerList = client.ListContainers();

            //Control plane API allows listing containers even for private storage
            //But listing blob will not be allowed if the storage is private
            //Therefore, this check is needed to verify if we can access blobs
            foreach (var container in containerList)
            {
                container.ListBlobs();
            }

            return Task.FromResult(data);
        }
    }
}
