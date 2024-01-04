// -----------------------------------------------------------------------
// <copyright file="AzureStorageService.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DaaS.Storage
{
    public interface IStorageService
    {
        Task<Uri> UploadFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken);
        Task DownloadFileAsync(string sourceFilePath, string destinationFilePath);
        Task DeleteFileAsync(string filePath);
        Task<IEnumerable<StorageFile>> GetFilesAsync(string directoryPath);
        void RemoveDirectory(string directoryPath);
        string GetBlobStorageHostName();
        bool ValidateStorageConfiguration(out string storageAccount, out Exception exceptionContactingStorage);
    }
}
