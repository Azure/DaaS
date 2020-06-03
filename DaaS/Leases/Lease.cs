using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Storage;

namespace DaaS.Leases
{
    abstract class Lease
    {
        public string Id;
        public string PathBeingLeased;
        internal DateTime ExpirationDate;

        public abstract void Renew();
        public abstract bool IsValid();

        public abstract void Release();

        public static bool IsValid(Lease lease)
        {
            return lease != null && lease.IsValid();
        }
    }
}
