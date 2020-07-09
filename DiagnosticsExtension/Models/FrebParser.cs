//-----------------------------------------------------------------------
// <copyright file="FrebParser.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace DiagnosticsExtension
{
    class FrebParser
    {
        public async static Task<IEnumerable<FrebFile>> GetFrebFiles()
        {
            List<FrebFile> listFrebFiles = new List<FrebFile>();
            string logFilePath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), "Logfiles");
            DirectoryInfo directory = new DirectoryInfo(logFilePath);

            var tasksList = new ConcurrentDictionary<FileAndFolder, Task<FrebFile>>();

            try
            {
                foreach (var frebFolder in directory.GetDirectories("W3SVC*"))
                {
                    foreach (var file in frebFolder.GetFiles("fr*.xml"))
                    {
                        var frebFileTask = GetDetailsFromFREBFile(file);

                        var fileAndFolder = new FileAndFolder()
                        {
                            File = file,
                            FolderName = frebFolder
                        };

                        tasksList.TryAdd(fileAndFolder, frebFileTask);
                    }
                }

                await Task.WhenAll(tasksList.Values);
                foreach (var item in tasksList)
                {
                    var frebFile = await item.Value;
                    if (frebFile != null)
                    {
                        frebFile.SiteId = item.Key.FolderName.Name.Replace("W3SVC", "");
                        frebFile.DateCreated = item.Key.File.CreationTime.ToUniversalTime();
                        frebFile.Href = GetRelativeLocation(item.Key.FolderName.Name, item.Key.File.Name);
                        listFrebFiles.Add(frebFile);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Message : Freb - Failed while looping files, Exception : {ex.ToString()}");
            }

            return listFrebFiles;
        }

        private static string GetRelativeLocation(string folder, string fileName)
        {
            return $"/api/vfs/logfiles/{folder}/{fileName}";
        }

        private static async Task<FrebFile> GetDetailsFromFREBFile(FileInfo file)
        {
            FrebFile frebFile = null;
            try
            {
                var fileText = await DaaS.FileSystemHelpers.ReadAllTextFromFileAsync(file.FullName);
                XmlDocument xmlDocument = new XmlDocument() { XmlResolver = null };
                xmlDocument.LoadXml(fileText);
                XmlNode xmlNode = xmlDocument.SelectSingleNode("failedRequest");
                frebFile = new FrebFile
                {
                    FileName = file.Name,
                    URL = xmlNode.Attributes["url"].Value,
                    Verb = xmlNode.Attributes["verb"].Value,
                    AppPoolName = xmlNode.Attributes["appPoolId"].Value,
                    TimeTaken = int.Parse(xmlNode.Attributes["timeTaken"].Value),
                    StatusCode = int.Parse(xmlNode.Attributes["statusCode"].Value.Split(new char[] { '.' })[0])
                };
            }
            catch (XmlException)
            {
            }
            catch (Exception ex)
            {
                throw new Exception($"Message : Get Freb Details - Failure calling GetDetailsFromFREBFile, File : {file}, Exception : {ex}");
            }


            return frebFile;
        }
    }

    class FileAndFolder
    {
        public DirectoryInfo FolderName { get; set; }

        public FileInfo File { get; set; }

    }
}
