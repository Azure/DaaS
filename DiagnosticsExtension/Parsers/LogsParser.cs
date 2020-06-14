//-----------------------------------------------------------------------
// <copyright file="LogsParser.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using DaaS;

namespace DiagnosticsExtension.Parsers
{
    public static class LogsParser
    {
        public static readonly long MB = 1024 * 1024;

        /// <summary>
        /// Get stdout logs from stdout log files in reverse chronological order of creation such that:
        /// - Any files of size maxPayloadBytes / 10 are only sampled up to this size. 
        ///     The result of this rule is that if all files are larger than maxBytes / 10, the most recent 10 files are sampled to build a payload of maxPayloadBytes size.       
        /// - Get all content from each file smaller than maxPayloadBytes / 10 in order until all files are read or maxPayloadBytes is reached.
        /// </summary>
        /// <param name="maxPayloadBytes">This will be set to a minimum of 1 MB by default</param>
        /// <param name="minFiles">The minimum number of files to sample. Max file size is determined by maxPayloadBytes / minFiles.</param>
        /// <returns></returns>
        internal static async Task<IEnumerable<LogFile>> GetStdoutLogFilesAsync(long maxPayloadBytes = 0, int minFiles = 10)
        {
            if (minFiles <= 0)
                minFiles = 1;

            if (maxPayloadBytes < MB)
                maxPayloadBytes = MB;

            var maxFileBytes = maxPayloadBytes / minFiles;
            
            string logFilePath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), "Logfiles");
            DirectoryInfo directory = new DirectoryInfo(logFilePath);

            var tasksList = new List<Task<LogFile>>();

            try
            {
                var filesToProcess = new List<FileInfo>();
                var allFiles = directory.GetFiles("stdout_*.log").OrderByDescending(x => x.CreationTimeUtc);
                long totalBytes = 0;
                foreach (var file in allFiles)
                {
                    var resultingFileSize = file.Length > maxFileBytes ? maxFileBytes : file.Length;

                    if (totalBytes + resultingFileSize <= maxPayloadBytes)
                    {
                        filesToProcess.Add(file);
                        totalBytes += resultingFileSize;
                    }
                }

                foreach (var file in filesToProcess)
                {
                    tasksList.Add(GetDetailsFromStdoutFileAsync(file, maxFileBytes));
                }

                return await Task.WhenAll(tasksList);
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed to iterate STDOUT log files", ex);
                throw;
            }
        }

        private static async Task<LogFile> GetDetailsFromStdoutFileAsync(FileInfo file, long maxFileBytes)
        {
            if (file.Length > maxFileBytes)
            {
                return await SampleLargeFileAsync(file, maxFileBytes);
            }

            var fileText = await FileSystemHelpers.ReadAllTextFromFileAsync(file.FullName);

            return new LogFile
            {
                CreationTimeUtc = file.CreationTimeUtc,
                FileName = file.Name,
                Content = fileText
            };
        }

        private static async Task<LogFile> SampleLargeFileAsync(FileInfo file, long maxFileBytes)
        {
            var sampleSize = 0;
            var lines = new List<string>();

            using (var fileStream = file.Open(FileMode.Open))
            using (var streamReader = new StreamReader(fileStream))
            {
                while (sampleSize < maxFileBytes)
                {
                    var line = await streamReader.ReadLineAsync();
                    var lineSize = streamReader.CurrentEncoding.GetByteCount(line);

                    if (sampleSize + lineSize > maxFileBytes)
                        break;

                    lines.Add(line);
                    sampleSize += lineSize;
                }
            }

            return new LogFile
            {
                CreationTimeUtc = file.CreationTimeUtc,
                FileName = file.Name,
                Content = string.Join("\n", lines)
            };
        }
    }
}
