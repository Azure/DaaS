// -----------------------------------------------------------------------
// <copyright file="Lease.cs" company="Microsoft Corporation">
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
