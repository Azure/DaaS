//-----------------------------------------------------------------------
// <copyright file="AnalysisRequest.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DaaS.Configuration;
using System;

namespace DaaS
{
    class AnalysisRequest
    {
        public DateTime StartTime { get; set; }
        public string LogFileName { get; set; }
        public DateTime ExpirationTime { get; set; }
        public string SessionId { get; set; }
        public int RetryCount { get; set; }
        public string BlobSasUri { get; set; }

        internal void SaveToDisk(string inprogressFile)
        {
            if (Settings.IsBlobSasUriConfiguredAsEnvironmentVariable() && Settings.IsSandBoxAvailable())
            {
                BlobSasUri = Settings.WebSiteDaasStorageSasUri;
            }

            this.ToJsonFile(inprogressFile);
        }
    }
}
