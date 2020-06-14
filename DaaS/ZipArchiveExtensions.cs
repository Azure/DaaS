//-----------------------------------------------------------------------
// <copyright file="ZipArchiveExtensions.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;


namespace DaaS
{
    public static class ZipArchiveExtensions
    {
        public static void AddFilesToZip(List<DaaSFileInfo> paths, ZipArchive zip)
        {
            foreach (var path in paths)
            {
                if (System.IO.File.Exists(path.FilePath))
                {
                    zip.AddFile(path.FilePath, path.Prefix, String.Empty);
                }
            }
        }
        private static string ForwardSlashCombine(string part1, string part2)
        {
            return Path.Combine(part1, part2).Replace('\\', '/');
        }

        public static void AddFile(this ZipArchive zipArchive, string filePath, string prefix, string directoryNameInArchive = "")
        {
            var fileInfo = new FileInfo(filePath);
            zipArchive.AddFile(fileInfo, prefix, directoryNameInArchive);
        }

        public static void AddFile(this ZipArchive zipArchive, FileInfoBase file, string prefix, string directoryNameInArchive)
        {
            Stream fileStream = null;
            try
            {
                fileStream = file.OpenRead();
            }
            catch (Exception ex)
            {
                // tolerate if file in use.
                // for simplicity, any exception.
                //tracer.TraceError(String.Format("{0}, {1}", file.FullName, ex));
                return;
            }

            try
            {
                string fileName = ForwardSlashCombine(directoryNameInArchive, file.Name);
                ZipArchiveEntry entry = zipArchive.CreateEntry(String.Concat(prefix, fileName), CompressionLevel.Fastest);
                entry.LastWriteTime = file.LastWriteTime;

                using (Stream zipStream = entry.Open())
                {
                    fileStream.CopyTo(zipStream);
                }
            }
            finally
            {
                fileStream.Dispose();
            }
        }
    }
}
