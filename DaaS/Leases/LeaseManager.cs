// -----------------------------------------------------------------------
// <copyright file="LeaseManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Configuration;
using DaaS.Storage;

namespace DaaS.Leases
{
    internal interface ILeaseManager
    {
        Lease TryGetLease(string pathToFile);
        Lease GetLease(string pathToFile);
    }

    class LeaseManager : ILeaseManager
    {
        private static LeaseManager _instance;
        public static LeaseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new LeaseManager();
                }
                return _instance;
            }
        }

        public Lease TryGetLease(string pathToFile)
        {
            try
            {
                string blobSasUri = Settings.Instance.BlobSasUri;
                if (!string.IsNullOrWhiteSpace(blobSasUri))
                {
                    return BlobLease.TryGetLease(pathToFile);
                }
                else
                {
                    return FileLease.TryGetLease(pathToFile);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not get the lease to " + pathToFile);
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                return null;
            }
        }

        public Lease GetLease(string pathToFile)
        {
            string blobSasUri = Settings.Instance.BlobSasUri;
            if (!string.IsNullOrWhiteSpace(blobSasUri))
            {
                return BlobLease.GetLease(pathToFile, blobSasUri);
            }
            else
            {
                return FileLease.GetLease(pathToFile);
            }
        }
    }
}
