// -----------------------------------------------------------------------
// <copyright file="IContainerClient.cs" company="Microsoft Corporation">
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
    public interface IContainerClient
    {
        public Task DeleteFileAsync(string filePath);
        public Task DownloadFileAsync(string sourceFilePath, string destinationFilePath);
        public Task<IEnumerable<StorageFile>> GetFilesAsync(string directoryPath);
        public Task RemoveDirectoryAsync(string directoryPath);
        public Task<Uri> UploadFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken);
    }
}
