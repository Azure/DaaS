// -----------------------------------------------------------------------
// <copyright file="CrashController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DaaS
{
    public class CrashController
    {

        const string DirectoryPath = "CrashDumps";
        private readonly string _siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "NoSiteFound";
        private readonly IStorageService _storageService;

        public CrashController(IStorageService storageService)
        {
            _storageService = storageService;
        }

        public async Task<List<CrashMonitoringFile>> GetCrashDumpsAsync(bool includeFullUri = false)
        {
            var filesCollected = new List<CrashMonitoringFile>();
            var files = await _storageService.GetFilesAsync($"{DirectoryPath}/{_siteName}");
            foreach ( var file in files )
            {
                filesCollected.Add(new CrashMonitoringFile(
                    file.Name,
                    includeFullUri ? file.Uri : null,
                    file.CreatedOn));
            }
            
            return filesCollected;
        }
    }
}
