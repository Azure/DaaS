using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Storage;

namespace DaaS.Leases
{
    internal interface ILeaseManager
    {
        Lease TryGetLease(string pathToFile, string blobSasUri);
        Lease GetLease(string pathToFile, string blobSasUri);
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

        public Lease TryGetLease(string pathToFile, string blobSasUri)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(blobSasUri))
                {
                    return BlobLease.TryGetLease(pathToFile, blobSasUri);
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

        public Lease GetLease(string pathToFile, string blobSasUri)
        {
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
