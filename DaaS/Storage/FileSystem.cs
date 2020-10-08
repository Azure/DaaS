//-----------------------------------------------------------------------
// <copyright file="FileSystem.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaaS.Configuration;
using Newtonsoft.Json;
using System.IO.Abstractions;

namespace DaaS
{
    public static class OperationManager
    {
        private const int DefaultRetries = 3;
        private const int DefaultDelayBeforeRetry = 250; // 250 ms

        public static void Attempt(Action action, int retries = DefaultRetries, int delayBeforeRetry = DefaultDelayBeforeRetry)
        {
            OperationManager.Attempt<object>(() =>
            {
                action();
                return null;
            }, retries, delayBeforeRetry);
        }

        public static T Attempt<T>(Func<T> action, int retries = DefaultRetries, int delayBeforeRetry = DefaultDelayBeforeRetry, Func<Exception, bool> shouldRetry = null)
        {
            T result = default(T);

            while (retries > 0)
            {
                try
                {
                    result = action();
                    break;
                }
                catch (Exception ex)
                {
                    if (shouldRetry != null && !shouldRetry(ex))
                    {
                        throw;
                    }

                    retries--;
                    if (retries == 0)
                    {
                        throw;
                    }
                }

                Thread.Sleep(delayBeforeRetry);
            }

            return result;
        }

        public static Task AttemptAsync(Func<Task> action, int retries = DefaultRetries, int delayBeforeRetry = DefaultDelayBeforeRetry)
        {
            return AttemptAsync(async () =>
            {
                await action();
                return true;
            }, retries, delayBeforeRetry);
        }

        public static async Task<TVal> AttemptAsync<TVal>(Func<Task<TVal>> action, int retries = DefaultRetries, int delayBeforeRetry = DefaultDelayBeforeRetry)
        {
            while (retries > 0)
            {
                try
                {
                    return await action();
                }
                catch
                {
                    retries--;
                    if (retries == 0)
                    {
                        throw;
                    }
                }

                await Task.Delay(delayBeforeRetry);
            }

            return default(TVal);
        }

        public static void SafeExecute(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // no-op
            }
        }

        public static T SafeExecute<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch
            {
                return default(T);
            }
        }
    }
    public static class FileSystemHelpers
    {
        public static string TmpFolder = EnvironmentVariables.DataPath;

        private static IFileSystem _default = new FileSystem();
        private static IFileSystem _instance;

        public static IFileSystem Instance
        {
            get { return _instance ?? _default; }
            set { _instance = value; }
        }

        public static Stream CreateFile(string path)
        {
            return File.Create(path);
        }

        public static void CreateDirectory(string path)
        {
            Directory.CreateDirectory(path);
        }

        public static string EnsureDirectory(string path)
        {
            if (!DirectoryExists(path))
            {
                CreateDirectory(path);
            }
            return path;
        }

        public static string EnsureDirectoryIgnoreAccessExceptions(string path)
        {
            try
            {
                return EnsureDirectory(path);
            }
            catch (UnauthorizedAccessException)
            {
                return path;
            }
        }

        public static void MoveDirectory(string sourceDirName, string destDirName)
        {
            // Instance.Directory.Move will result in access denied sometime. Do it ourself!

            EnsureDirectory(destDirName);

            string[] files = Instance.Directory.GetFiles(sourceDirName);
            string[] dirs = Instance.Directory.GetDirectories(sourceDirName);

            foreach (var filePath in files)
            {
                var fi = new FileInfo(filePath);
                MoveFile(filePath, Path.Combine(destDirName, fi.Name));
            }

            foreach (var dirPath in dirs)
            {
                var di = new DirectoryInfo(dirPath);
                MoveDirectory(dirPath, Path.Combine(destDirName, di.Name));
                Instance.Directory.Delete(dirPath, false);
            }

            Instance.Directory.Delete(sourceDirName, false);
        }

        public static bool FileExists(string path)
        {
            return Instance.File.Exists(path);
        }

        public static bool DirectoryExists(string path)
        {
            return Instance.Directory.Exists(path);
        }

        public static bool IsSubfolder(string parent, string child)
        {
            // normalize
            string parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string childPath = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase);
        }

        public static Stream OpenFile(string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileShare share = FileShare.None)
        {
            return Instance.File.Open(path, mode, access, share);
        }

        public static Stream OpenRead(string path)
        {
            return Instance.File.OpenRead(path);
        }

        public static string ReadAllText(string path)
        {
            return Instance.File.ReadAllText(path);
        }

        public static string[] ReadAllLines(string path)
        {
            return Instance.File.ReadAllLines(path);
        }

        /// <summary>
        /// Replaces File.ReadAllText,
        /// Will do the same thing only this can work on files that are already open (and share read/write).
        /// </summary>
        public static string ReadAllTextFromFile(string path)
        {
            using (Stream fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var streamReader = new StreamReader(fileStream);
                return streamReader.ReadToEnd();
            }
        }

        /// <summary>
        /// Async version of ReadAllTextFromFile,
        /// </summary>
        public static async Task<string> ReadAllTextFromFileAsync(string path)
        {
            using (var fileStream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var streamReader = new StreamReader(fileStream))
            {
                return await streamReader.ReadToEndAsync();
            }
        }

        public static Stream OpenWrite(string path)
        {
            return Instance.File.OpenWrite(path);
        }

        public static void WriteAllText(string path, string contents)
        {
            Instance.File.WriteAllText(path, contents);
        }
        public static DateTime GetLastWriteTimeUtc(string path)
        {
            return Instance.File.GetLastWriteTimeUtc(path);
        }

        public static void WriteAllBytes(string path, byte[] contents)
        {
            Instance.File.WriteAllBytes(path, contents);
        }

        /// <summary>
        /// Replaces File.WriteAllText,
        /// Will do the same thing only this can work on files that are already open (and share read/write).
        /// </summary>
        public static void WriteAllTextToFile(string path, string content)
        {
            using (Stream fileStream = OpenFile(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var streamWriter = new StreamWriter(fileStream);
                streamWriter.Write(content);
                streamWriter.Flush();
            }
        }

        /// <summary>
        /// Async version of WriteAllTextToFile,
        /// </summary>
        public static async Task WriteAllTextToFileAsync(string path, string content)
        {
            using (var fileStream = OpenFile(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            using (var streamWriter = new StreamWriter(fileStream))
            {
                await streamWriter.WriteAsync(content);
                await streamWriter.FlushAsync();
            }
        }

        /// <summary>
        /// Replaces File.AppendAllText,
        /// Will do the same thing only this can work on files that are already open (and share read/write).
        /// </summary>
        public static void AppendAllTextToFile(string path, string content)
        {
            using (Stream fileStream = OpenFile(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                var streamWriter = new StreamWriter(fileStream);
                streamWriter.Write(content);
                streamWriter.Flush();
            }
        }

        public static void CopyFile(string sourceFileName, string destFileName, bool overwrite = true)
        {
            Instance.File.Copy(sourceFileName, destFileName, overwrite);
        }

        public static void MoveFile(string sourceFileName, string destFileName)
        {
            FileSystemHelpers.DeleteFileSafe(destFileName);
            Instance.File.Move(sourceFileName, destFileName);
        }

        // From MSDN: http://msdn.microsoft.com/en-us/library/bb762914.aspx
        public static void CopyDirectoryRecursive(string sourceDirPath, string destinationDirPath, bool overwrite = true)
        {
            // Get the subdirectories for the specified directory.
            var sourceDir = new DirectoryInfo(sourceDirPath);

            if (!sourceDir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirPath);
            }

            // If the destination directory doesn't exist, create it.
            if (!DirectoryExists(destinationDirPath))
            {
                CreateDirectory(destinationDirPath);
            }

            // Get the files in the directory and copy them to the new location.
            foreach (FileSystemInfo sourceFileSystemInfo in sourceDir.EnumerateFileSystemInfos())
            {
                var sourceFile = sourceFileSystemInfo as FileInfo;
                if (sourceFile != null)
                {
                    string destinationFilePath = Path.Combine(destinationDirPath, sourceFile.Name);
                    Instance.File.Copy(sourceFile.FullName, destinationFilePath, overwrite);
                }
                else
                {
                    var sourceSubDir = sourceFileSystemInfo as DirectoryInfo;
                    if (sourceSubDir != null)
                    {
                        // Copy sub-directories and their contents to new location.
                        string destinationSubDirPath = Path.Combine(destinationDirPath, sourceSubDir.Name);
                        CopyDirectoryRecursive(sourceSubDir.FullName, destinationSubDirPath, overwrite);
                    }
                }
            }
        }

        public static void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
        {
            Instance.File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }

        public static DateTime GetDirectoryLastWriteTimeUtc(string path)
        {
            return Instance.Directory.GetLastWriteTimeUtc(path);
        }

        public static void SetDirectoryLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc)
        {
            Instance.Directory.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }

        public static FileInfoBase FileInfoFromFileName(string fileName)
        {
            return Instance.FileInfo.FromFileName(fileName);
        }

        public static DirectoryInfoBase DirectoryInfoFromDirectoryName(string path)
        {
            return Instance.DirectoryInfo.FromDirectoryName(path);
        }

        public static string GetFullPath(string path)
        {
            return Instance.Path.GetFullPath(path);
        }

        public static string[] GetFileSystemEntries(string path)
        {
            return Instance.Directory.GetFileSystemEntries(path);
        }

        public static string[] GetDirectories(string path)
        {
            return Instance.Directory.GetDirectories(path);
        }

        public static string[] GetFiles(string path, string pattern)
        {
            return Instance.Directory.GetFiles(path, pattern);
        }

        public static string[] GetFiles(string path, string pattern, SearchOption searchOption)
        {
            return Instance.Directory.GetFiles(path, pattern, searchOption);
        }

        public static IEnumerable<string> ListFiles(string path, SearchOption searchOption, params string[] lookupList)
        {
            if (!Directory.Exists(path))
            {
                return Enumerable.Empty<string>();
            }

            // Only lookup of type *.extension or path\file (no *) is supported
            if (lookupList.Any(lookup => lookup.LastIndexOf('*') > 0))
            {
                throw new NotSupportedException("lookup with a '*' that is not the first character is not supported");
            }

            lookupList = lookupList.Select(lookup => lookup.TrimStart('*')).ToArray();

            return Directory.EnumerateFiles(path, "*.*", searchOption)
                            .Where(filePath => lookupList.Any(lookup => filePath.EndsWith(lookup, StringComparison.OrdinalIgnoreCase)));
        }

        public static void DeleteFile(string path)
        {
            Instance.File.Delete(path);
        }

        public static void DeleteFileSafe(string path, bool ignoreErrors = true)
        {
            var info = Instance.FileInfo.FromFileName(path);
            DeleteFileSystemInfo(info, ignoreErrors);
        }

        public static void DeleteDirectorySafe(string path, bool ignoreErrors = true)
        {
            DeleteFileSystemInfo(Instance.DirectoryInfo.FromDirectoryName(path), ignoreErrors);
        }

        public static void DeleteDirectoryContentsSafe(string path, bool ignoreErrors = true)
        {
            DeleteDirectoryContentsSafe(Instance.DirectoryInfo.FromDirectoryName(path), ignoreErrors);
        }

        public static bool IsFileSystemReadOnly()
        {
            try
            {
                string folder = Path.Combine(TmpFolder, Guid.NewGuid().ToString());
                CreateDirectory(folder);
                DeleteDirectorySafe(folder, ignoreErrors: false);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        public static string GetDirectoryName(string path)
        {
            return Instance.Path.GetDirectoryName(path);
        }

        private static void DeleteDirectoryContentsSafe(DirectoryInfoBase directoryInfo, bool ignoreErrors)
        {
            try
            {
                if (directoryInfo.Exists)
                {
                    foreach (var fsi in directoryInfo.GetFileSystemInfos())
                    {
                        DeleteFileSystemInfo(fsi, ignoreErrors);
                    }
                }
            }
            catch
            {
                if (!ignoreErrors) throw;
            }
        }

        private static void DeleteFileSystemInfo(FileSystemInfoBase fileSystemInfo, bool ignoreErrors)
        {
            if (!fileSystemInfo.Exists)
            {
                return;
            }

            try
            {
                fileSystemInfo.Attributes = FileAttributes.Normal;
            }
            catch
            {
                if (!ignoreErrors) throw;
            }

            var directoryInfo = fileSystemInfo as DirectoryInfoBase;

            if (directoryInfo != null)
            {
                DeleteDirectoryContentsSafe(directoryInfo, ignoreErrors);
            }

            DoSafeAction(fileSystemInfo.Delete, ignoreErrors);
        }

        private static void DoSafeAction(Action action, bool ignoreErrors)
        {
            try
            {
                OperationManager.Attempt(action);
            }
            catch
            {
                if (!ignoreErrors) throw;
            }
        }

        public static void WriteFile(string path, string contents)
        {
            using (FileStream fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                using (StreamWriter sw = new StreamWriter(fs))
                {
                    sw.Write(contents);
                }
            }
        }
        public static string ReadFile(string path)
        {
            string contents = "";
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using (StreamReader sr = new StreamReader(fs))
                {
                    contents = sr.ReadToEnd();
                }
            }
            return contents;
        }
        public static void CreateDirectoryIfNotExists(string folder)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }

        public static void CreateDirectoryIfNotExistsSafe(string folder)
        {
            try
            {
                CreateDirectoryIfNotExists(folder);
            }
            catch(Exception)
            {
                //no-op
            }
        }

        public static List<string> GetFilesInDirectory(string directoryPath, string searchPattern = "*", bool isRelativePath = true, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            string fullDirPath = directoryPath;
            if (isRelativePath)
            {
                string rootPath = Settings.UserSiteStorageDirectory;
                fullDirPath = Path.Combine(rootPath, directoryPath).ConvertForwardSlashesToBackSlashes();
            }

            var files = new List<string>();
            if (Directory.Exists(fullDirPath))
            {
                files = Directory.GetFiles(fullDirPath, searchPattern, searchOption).ToList();
            }
            return files;
        }

        public static T FromJsonFile<T>(string filePath)
        {
            string fileContents = ReadFile(filePath);
            T obj = JsonConvert.DeserializeObject<T>(fileContents);
            return obj;
        }
    }
    public static class ObjectExtensions
    {
        public static void ToJsonFile(this object objectToSerialize, string filePath)
        {
            FileSystemHelpers.WriteFile(filePath, JsonConvert.SerializeObject(objectToSerialize, Formatting.Indented));
        }
        
    }    
}
