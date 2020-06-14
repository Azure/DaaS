//-----------------------------------------------------------------------
// <copyright file="BlobLease.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using DaaS.Configuration;
using DaaS.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace DaaS.Leases
{
    internal class BlobLease : Lease
    {
        private CloudBlockBlob _fileBlob;

        private BlobLease() { }

        public static BlobLease TryGetLease(string pathToFile, string blobSasUri, string leaseId = null)
        {
            BlobLease lease = null;

            try
            {
                Logger.LogInfo($"Inside TryGetLease method with {pathToFile} and lease {leaseId == null}");
                var blob = BlobController.GetBlobForFile(pathToFile, blobSasUri);
                if (!blob.Exists())
                {
                    // This was a path blob. We want to create a new blob to hold the lease
                    // Luckily for us, just uploading some text to it does the trick
                    blob.UploadText("LeaseHolder");
                }

                leaseId = string.IsNullOrEmpty(leaseId) ? Guid.NewGuid().ToString() : leaseId;
                var leaseDuration = Infrastructure.Settings.LeaseDuration;
                lease = new BlobLease()
                {
                    PathBeingLeased = pathToFile,
                    ExpirationDate = DateTime.UtcNow + leaseDuration,
                    _fileBlob = blob
                };
                try
                {
                    Logger.LogInfo($"Acquiring Lease");
                    lease.Id = blob.AcquireLease(leaseDuration, leaseId);
                }
                catch (StorageException ex)
                {
                    Logger.LogErrorEvent("Got StorageException while acquiring lease" , ex);
                    // Someone else is holding the lease
                    return null;
                }
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed in TryGetLease method", ex);
            }
            return lease;

        }

        public static BlobLease GetLease(string pathToFile, string blobSasUri)
        {
            while (true)
            {
                var lease = TryGetLease(pathToFile, blobSasUri);
                if (lease != null && lease.IsValid())
                {
                    return lease;
                }
                Thread.Sleep(TimeSpan.FromSeconds(5));
            }
        }

        public override void Renew()
        {
            try
            {
                _fileBlob.RenewLease(AccessCondition.GenerateLeaseCondition(this.Id));
            }
            catch (Exception)
            {
                // Lease has expired. Try to renew it (this is mainly useful for debugging)
                TryGetLease(this.PathBeingLeased, this.Id);
            }
        }

        public override bool IsValid()
        {
            return ExpirationDate > DateTime.UtcNow;
        }

        public override void Release()
        {
            _fileBlob.ReleaseLease(AccessCondition.GenerateLeaseCondition(Id));
        }
    }
}
