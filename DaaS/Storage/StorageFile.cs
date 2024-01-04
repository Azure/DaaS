// -----------------------------------------------------------------------
// <copyright file="StorageFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DaaS.Storage
{
    public class StorageFile
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public DateTimeOffset? LastModified { get; set; }
        public Uri Uri { get; internal set; }
        public long? Size { get; internal set; }
        public DateTimeOffset? CreatedOn { get; internal set; }
    }
}
