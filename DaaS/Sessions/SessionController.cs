// -----------------------------------------------------------------------
// <copyright file="SessionController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using DaaS.Configuration;
using Azure.Storage.Blobs;

namespace DaaS.Sessions
{
    public class SessionController
    {
        const string ContainerName = "memorydumps";

        private static readonly object _daasVersionUpdateLock = new object();
        private static bool _daasVersionCheckInProgress = false;

        public string BlobStorageSasUri
        {
            get
            {
                var blobSasUri = Settings.Instance.BlobSasUri;
                if (!string.IsNullOrEmpty(blobSasUri))
                {
                    return blobSasUri;
                }

                return Settings.Instance.AccountSasUri;
            }
        }

        public string StorageConnectionString
        {
            get
            {
                return Settings.Instance.StorageConnectionString;
            }
        }

        public bool IsSandboxAvailable()
        {
            return Settings.Instance.IsSandBoxAvailable();
        }

        public void StartSessionRunner()
        {
            var daasDisabled = Environment.GetEnvironmentVariable("WEBSITE_DAAS_DISABLED");
            if (daasDisabled != null && daasDisabled.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                DeleteWebjobFolderIfExists(EnvironmentVariables.DaasWebJobAppData);
                DeleteWebjobFolderIfExists(EnvironmentVariables.DaasWebJobDirectory);
                CleanUpObsoleteFiles();
                return;
            }

            if (_daasVersionCheckInProgress)
            {
                //
                // Another thread updating DaasRunner
                // so don't do anything right now
                //

                Logger.LogVerboseEvent("Another check to update DaaS bits is in progress");
                return;
            }


            lock (_daasVersionUpdateLock)
            {
                _daasVersionCheckInProgress = true;
            }

            try
            {
                Logger.LogVerboseEvent("Checking DaaS bits and updating if required");
                FileSystemHelpers.CreateDirectoryIfNotExists(EnvironmentVariables.DaasWebJobDirectory);
                FileSystemHelpers.CreateDirectoryIfNotExists(EnvironmentVariables.DaasConsoleDirectory);

                string newDaasRunner = Path.Combine(Infrastructure.GetDaasInstallationPath(), "bin", "daasrunner.exe");
                string oldDaasRunner = EnvironmentVariables.DaasRunner;

                string newDaasConsole = Path.Combine(Infrastructure.GetDaasInstallationPath(), "bin", "daasconsole.exe");
                string oldDaasConsole = EnvironmentVariables.DaasConsole;

                if (IsDaasRunnerVersionLower(newDaasRunner, oldDaasRunner))
                {
                    CopyFileWithRetry(newDaasRunner, targetFile: oldDaasRunner);
                    CopyFileWithRetry($"{newDaasRunner}.config", targetFile: $"{oldDaasRunner}.config");
                }

                if (IsFileVersionLower(newDaasConsole, oldDaasConsole))
                {
                    CopyFileWithRetry(newDaasConsole, targetFile: oldDaasConsole);
                    CopyFileWithRetry($"{newDaasConsole}.config", targetFile: $"{oldDaasConsole}.config");
                }

                CleanUpObsoleteFiles();
                Logger.LogVerboseEvent("Done checking DaaS bits for any new updates");
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed while checking or updating DaaSRunner", ex);
            }
            finally
            {
                lock (_daasVersionUpdateLock)
                {
                    _daasVersionCheckInProgress = false;
                }
            }
        }

        public bool IsValidStorageConnectionString(out string storageConnectionStringEx)
        {
            string connectionString = Settings.Instance.StorageConnectionString;
            storageConnectionStringEx = "";
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return false;
            }

            try
            {
                var containerClient = new BlobContainerClient(connectionString, ContainerName);
                containerClient.Exists();
                return true;
            }
            catch (Exception ex)
            {
                storageConnectionStringEx = ex.ToLogString();
            }

            return false;
        }

        private void CleanUpObsoleteFiles()
        {
            try
            {
                DeleteWebjobFolderIfExists(EnvironmentVariables.DaasWebJobAppData);
                DeleteOlderDlls(EnvironmentVariables.DaasConsoleDirectory);
                DeleteOlderDlls(EnvironmentVariables.DaasWebJobDirectory);
                DeletePrivateSettingsXml();
            }
            catch (Exception ex)
            {
                Logger.LogWarningEvent("Failed while cleaning up obsolete files", ex);
            }
        }

        private void DeletePrivateSettingsXml()
        {
            File.Delete(Path.Combine(EnvironmentVariables.DaasDirectory, "PrivateSettings.xml"));
        }

        private void DeleteOlderDlls(string directoryPath)
        {
            var dlls = FileSystemHelpers.GetFilesInDirectory(
                directoryPath,
                "*.dll",
                isRelativePath: false,
                SearchOption.TopDirectoryOnly);

            foreach (var dll in dlls)
            {
                FileSystemHelpers.DeleteFileSafe(dll);
                Logger.LogVerboseEvent($"DeleteOlderDlls - deleted {dll}");
            }
        }

        private bool DeleteWebjobFolderIfExists(string fullPath)
        {
            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.EnumerateFiles(fullPath))
                {
                    RetryHelper.RetryOnException("Deleting webjob from AppData if exists...", () =>
                    {
                        System.IO.File.Delete(file);
                    }, TimeSpan.FromSeconds(1));
                }

                try
                {
                    Directory.Delete(fullPath);
                }
                catch (Exception)
                {
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool IsFileVersionLower(string newFile, string oldFile)
        {
            if (!FileSystemHelpers.FileExists(oldFile))
            {
                //
                // If the file does not exist, return false
                // to ensure that newer bits get copied
                //
                Logger.LogVerboseEvent($"{oldFile} does not exist, new bits will be copied");
                return true;
            }

            Version newVersion = GetFileVersion(newFile);
            Version oldVersion = GetFileVersion(oldFile);
            var fileName = Path.GetFileName(newFile);

            var result = oldVersion.CompareTo(newVersion);

            if (result >= 0)
            {
                Logger.LogVerboseEvent($"[{fileName}] New version ({newVersion}) is the same as existing version {oldVersion}. Version Compare result = {result}");
                return false;
            }

            Logger.LogVerboseEvent($"[{fileName}] Current version : {oldVersion} is lower than version in Daas installation path : {newVersion}, new bits will be copied");
            return true;
        }

        private static bool IsDaasRunnerVersionLower(string newDaasRunner, string oldDaasRunner)
        {
            if (!FileSystemHelpers.FileExists(oldDaasRunner))
            {
                Logger.LogVerboseEvent($"Found no DaasRunner in {oldDaasRunner}");
                return true;
            }

            var oldVersion = GetDaasRunnerVersion();
            if (oldVersion == null)
            {
                //
                // If we are not able to fetch DaasRunner version then
                // we also don't copy anything for web job
                //

                return false;
            }

            if (oldVersion.Major == 0)
            {
                return IsFileVersionLower(newDaasRunner, oldDaasRunner);
            }

            Version newVersion = GetFileVersion(newDaasRunner);
            var result = oldVersion.CompareTo(newVersion);

            if (result >= 0)
            {
                Logger.LogVerboseEvent($"[DaasRunner] New version ({newVersion}) is the same as existing version {oldVersion}. Version Compare result = {result}");
                return false;
            }

            Logger.LogVerboseEvent($"[DaasRunner] Current version : {oldVersion} is lower than version in Daas installation path : {newVersion}, new bits will be copied");
            return true;
        }

        private static Version GetDaasRunnerVersion()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("DaaSRunner");

                if (processes.Length > 0)
                {
                    string path = processes.FirstOrDefault().GetMainModuleFileName();
                    var daasRunnerVersion = GetFileVersion(path);
                    Logger.LogVerboseEvent($"Found DaasRunner process running in {path} with version {daasRunnerVersion}");
                    return daasRunnerVersion;
                }
                else
                {
                    Logger.LogVerboseEvent($"DaasRunner process not running");
                    return new Version(0, 0, 0, 0);
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Error occured while getting DaasRunner version", ex);
            }

            return null;
        }

        private static void CopyFileWithRetry(string sourceFile, string targetFile)
        {
            string file = Path.GetFileName(sourceFile);
            RetryHelper.RetryOnException($"Copying file {file} from {sourceFile} to {targetFile}...", () =>
            {
                if (File.Exists(sourceFile))
                {
                    Logger.LogVerboseEvent($"Copying file {file} from {sourceFile} to {targetFile}");
                    File.Copy(sourceFile, targetFile, true);
                    Logger.LogVerboseEvent($"File {file} copied successfully");
                }

            }, TimeSpan.FromSeconds(1), 3, true, false);
        }

        private static Version GetFileVersion(string filePath)
        {
            Version ver = new Version(0, 0, 0, 0);
            var fileVersion = FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            try
            {
                ver = Version.Parse(fileVersion);
            }
            catch (Exception)
            {
            }
            return ver;
        }
    }
}
