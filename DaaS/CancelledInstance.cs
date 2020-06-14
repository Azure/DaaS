//-----------------------------------------------------------------------
// <copyright file="CancelledInstance.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DaaS.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DaaS
{

    [Serializable]
    public class CancelledInstance
    {

        public string Name { get; set; }
        public string ProcessCleanupOnCancel { get; set; }
        public DateTime CancellationTime { get; set; }
        public string DiagnoserName { get; set; }

        public string SessionId { get; set; }

        public async Task DeleteFile()
        {
            var cancelledFilePath = Path.Combine(Settings.SiteRootDir, @"data\DaaS", Settings.CancelledDir, $"{Name}.{DiagnoserName}");

            if (Infrastructure.Storage.FileExists(cancelledFilePath, Storage.StorageLocation.UserSiteData))
            {
                await Infrastructure.Storage.DeleteFileAsync(cancelledFilePath);
                Logger.LogVerboseEvent($"Successfully deleted CancelledInstance file - {cancelledFilePath}");
            }
        }
    }
}
