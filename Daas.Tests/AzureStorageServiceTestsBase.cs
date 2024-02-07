// -----------------------------------------------------------------------
// <copyright file="AzureStorageServiceTestsBase.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DaaS.Storage;
using Xunit;

namespace Daas.Test
{
    public abstract class AzureStorageServiceTestsBase
    {
        private AzureStorageService _azureStorageService;
        private readonly string _folderPath;

        public AzureStorageServiceTestsBase(AzureStorageService azureStorageService, string folderPath)
        {
            _azureStorageService = azureStorageService;
            _folderPath = folderPath;
        }

        [Fact]
        public async Task FileUpload()
        {
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
            string fileName = GetFileName();

            string testFile = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(testFile, "someContent");
            var fileUploadUri = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/{fileName}", default);

            Assert.NotNull(fileUploadUri);
            Assert.Contains($"/{fileName}", fileUploadUri.AbsoluteUri);
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
        }

        [Fact]
        public async Task FileDownload()
        {
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
            string fileName = GetFileName();

            string testFile = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(testFile, "someContent");
            var fileUploadUri = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/{fileName}", default);

            Assert.NotNull(fileUploadUri);
            Assert.Contains($"/{fileName}", fileUploadUri.AbsoluteUri);

            var downloadPath = Path.Combine(Path.GetTempPath(), "downloaded.txt");
            await _azureStorageService.DownloadFileAsync($"{_folderPath}/{fileName}", downloadPath);

            Assert.True(File.Exists(downloadPath));
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
        }

        [Fact]
        public async Task ListFiles()
        {
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
            string fileName = GetFileName();

            string testFile = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(testFile, "someContent");
            var fileSize = new FileInfo(testFile).Length;
            var fileUploadUri = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/{fileName}", default);

            Assert.NotNull(fileUploadUri);
            Assert.Contains($"/{fileName}" , fileUploadUri.AbsoluteUri);

            var files = await _azureStorageService.GetFilesAsync(_folderPath);
            Assert.NotNull(files);

            var firstFile = files.FirstOrDefault();
            Assert.NotNull(firstFile);

            Assert.Contains($"/{fileName}", firstFile.Uri.AbsoluteUri);
            Assert.Contains($"/{fileName}", firstFile.FullPath);
            Assert.True(firstFile.Name == fileName, "File name matches");
            Assert.NotNull(firstFile.CreatedOn);
            Assert.True(firstFile.Size > 0, "File Size greater than 0");
            Assert.Equal(firstFile.Size, fileSize);

            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
        }

        [Fact]
        public async Task DeleteFile()
        {
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
            string fileName = GetFileName();

            string testFile = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(testFile, "someContent");
            var fileUploadUri = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/{fileName}", default);

            Assert.NotNull(fileUploadUri);
            Assert.Contains($"/{fileName}" , fileUploadUri.AbsoluteUri);

            await _azureStorageService.DeleteFileAsync($"{_folderPath}/{fileName}");

            var files = await _azureStorageService.GetFilesAsync(_folderPath);
            Assert.Empty(files);

            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
        }

        [Fact]
        public async Task RemoveDirectory()
        {
            await _azureStorageService.RemoveDirectoryAsync(_folderPath);
            string fileName = GetFileName();

            string testFile = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllText(testFile, "someContent");
            
            var fileUploadUri1 = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/test1.txt", default);
            var fileUploadUri2 = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/test2.txt", default);
            var fileUploadUri3 = await _azureStorageService.UploadFileAsync(testFile, $"{_folderPath}/test3.txt", default);

            Assert.NotNull(fileUploadUri1);
            Assert.Contains("/test1.txt" , fileUploadUri1.AbsoluteUri);

            Assert.NotNull(fileUploadUri2);
            Assert.Contains("/test2.txt", fileUploadUri2.AbsoluteUri);

            Assert.NotNull(fileUploadUri3);
            Assert.Contains("/test3.txt", fileUploadUri3.AbsoluteUri);

            var files = await _azureStorageService.GetFilesAsync(_folderPath);
            Assert.NotNull(files);
            Assert.True(files.Count() == 3);

            await _azureStorageService.RemoveDirectoryAsync(_folderPath);

            files = await _azureStorageService.GetFilesAsync(_folderPath);
            Assert.NotNull(files);
            Assert.Empty(files);
        }

        private string GetFileName()
        {
            return $"File-{DateTime.UtcNow.Ticks}.txt";
        }

    }
}
