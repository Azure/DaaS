// -----------------------------------------------------------------------
// <copyright file="FileLease.cs" company="Microsoft Corporation">
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

namespace DaaS.Leases
{

    [Serializable]
    class FileLease : Lease
    {
        private FileLease(String id, string pathBeingLeased)
        {
            Id = id;
            PathBeingLeased = pathBeingLeased;
            ExpirationDate = DateTime.MinValue;
        }

        public FileLease() { }

        public static FileLease TryGetLease(string pathToFile)
        {
            //FileLease lease = new FileLease(pathToLease);
            // Does someone already have the lease
            // Try to acquire the lease
            // TODO: Implement
            return new FileLease();
        }
        public static FileLease GetLease(string pathToFile)
        {
            // TODO: Implement
            return new FileLease();
        }

        public override void Renew()
        {
            // TODO: Implement
        }

        public override bool IsValid()
        {
            // TODO: Implement
            return true;
        }

        public override void Release()
        {
            // TODO: Implement
        }
    }
}

/*
    internal class FileLeaseManager : ILeaseManager
    {

        private static FileLeaseManager _instance;
        public static FileLeaseManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FileLeaseManager();
                }
                return _instance;
            }
        }

        public Lease TryGetLease(string pathToFile)
        {
            var leaseRequest = LeaseRequest.GetLeaseRequest(pathToFile);
            if (leaseRequest.IsSomeoneHoldingTheLease())
            {
                return null;
            }

            leaseRequest.IsALeaseAcquisitionGoingOn();

            leaseRequest.CompeteForLease();



            return new Lease();
        }

        // blocks untill lease is acquired
        public Lease GetLease(string pathToFile)
        {
            return new Lease();
        }

        public bool RenewLease(Lease lease)
        {
            return true;
        }

        public bool ReleaseLease(Lease lease)
        {
            return true;
        }

        public bool IsValidLease(Lease lease)
        {
            return true;
        }
    }


    class LeaseRequest : File
    {
        private string _pathBeingLeased;
        private string _leaseFilePath;
        public Instance _requestingInstance;
        public DateTime ExpirationTime = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        protected override StorageLocation StorageLocation
        {

            get { throw new NotImplementedException(); 
                //return Lease.LeaseStorageLocation;
            }
        }

        public static LeaseRequest GetLeaseRequest(string pathBeingLeased)
        {
            var leaseRequest = new LeaseRequest()
            {
                _pathBeingLeased = pathBeingLeased,
                _leaseFilePath = GetPathToLease(pathBeingLeased),
                _requestingInstance = Instance.GetCurrentInstance(),
            };
            leaseRequest.RelativePath = leaseRequest.GetLeaseRequestPath();

            return leaseRequest;
        }

        public bool IsSomeoneHoldingTheLease()
        {
            if (!Infrastructure.Storage.FileExistsAsync(_leaseFilePath, StorageLocation).Result)
            {
                return false;
            }

            // Check to see if the lease is expired
            using (var leaseStream = Infrastructure.Storage.ReadFileAsync(_leaseFilePath, StorageLocation).Result)
            {
                Lease lease = Lease.Open(leaseStream);
                if (lease.IsValid())
                {
                    return true;
                }
                 
                // Lease has expired. Let's delete it
                lease.DeleteAsync();
                return false;
            }
        }

        public bool IsAlive()
        {
            return ExpirationTime < DateTime.UtcNow;
        }

        public void SubmitLeaseRequest()
        {
            this.SaveAsync().Wait();
        }

        private string GetLeaseRequestPath()
        {
            return Path.Combine(_leaseFilePath, _requestingInstance.Name);
        }

        private string GetLeaseAcquisitionInProgressPath()
        {
            return Path.Combine(_leaseFilePath, "LeaseAcquisitionInProgress");
        }

        public async Task<bool> IsAnyoneRequestingThisLease()
        {
            return (await Infrastructure.Storage.GetFilesInDirectory(_leaseFilePath, StorageLocation)).Count > 0;
        }

        public async Task DeleteAllExpiredRequests()
        {
            foreach (
                var leaseRequestPath in
                    await Infrastructure.Storage.GetFilesInDirectory(_leaseFilePath, StorageLocation))
            {
                using (var stream = await Infrastructure.Storage.ReadFileAsync(leaseRequestPath, StorageLocation))
                {
                    var leaseRequest = LeaseRequest.Open(stream);
                    if (leaseRequest.IsAlive())
                    {
                        await Infrastructure.Storage.DeleteFileAsync(leaseRequest);
                    }
                }
            }
        }

        public void CancelLeaseRequest()
        {
            throw new NotImplementedException();
        }

        public Lease AcquireLease()
        {
            throw new NotImplementedException();
        }


        private static string GetPathToLease(string pathBeingLeased)
        {
            // Remove any prepending slashes
            if (pathBeingLeased.StartsWith("\\") || pathBeingLeased.StartsWith("/"))
            {
                pathBeingLeased = pathBeingLeased.Remove(0, 1);
            }

            return Path.Combine("Leases", pathBeingLeased);
        }

        internal static LeaseRequest Open(Stream leaseRequestStream)
        {
            return leaseRequestStream.LoadFromXmlStream<LeaseRequest>();
        }

        internal override async Task<Stream> GetFileStreamAsync()
        {
            return this.GetXmlStream();
        }

        internal bool IsALeaseAcquisitionGoingOn()
        {
            LeaseAcquisitionInProgress leaseAcquisitionInProgressFile = new LeaseAcquisitionInProgress(_pathBeingLeased);

            if ( Infrastructure.Storage.FileExistsAsync(leaseAcquisitionInProgressFile.RelativePath, StorageLocation)
                    .Result)
            {
                return false;
            }

            DateTime expirationTime;
            using ( var fileStream = Infrastructure.Storage.ReadFileAsync(leaseAcquisitionInProgressFile.RelativePath,
                        StorageLocation).Result)
            {
                StreamReader sr = new StreamReader(fileStream);
                expirationTime = DateTime.Parse(sr.ReadToEnd());
            }
            if (DateTime.UtcNow > expirationTime)
            {
                Infrastructure.Storage.DeleteFileAsync(leaseAcquisitionInProgressFile).Wait();
            }

            return true;
        }



        internal Lease CompeteForLease()
        {
            if (IsALeaseAcquisitionGoingOn())
            {
                return null;
            }

            Infrastructure.Storage.SaveFileAsync(
                new MemoryStream(Encoding.UTF8.GetBytes(DateTime.UtcNow.AddSeconds(30).ToString("s"))),
                GetLeaseAcquisitionInProgressPath(), StorageLocation);

            throw new NotImplementedException();
        }
    }

    public class LeaseAcquisitionInProgress : File
    {
        public DateTime ExpirationTime;
        protected override StorageLocation StorageLocation
        {
            get
            {
                throw new NotImplementedException();
                //return Lease.LeaseStorageLocation;
            }
        }

        public LeaseAcquisitionInProgress(string pathToBeLeased)
        {
            ExpirationTime = DateTime.Now.AddSeconds(30);
            RelativePath = pathToBeLeased;
            FileName = Path.GetFileName(pathToBeLeased);
        }

        internal override async Task<Stream> GetFileStreamAsync()
        {
            return this.GetXmlStream();
        }

        internal static LeaseAcquisitionInProgress Open(Stream fileStream)
        {
            return fileStream.LoadFromXmlStream<LeaseAcquisitionInProgress>();
        }
    }
}
 
 */
