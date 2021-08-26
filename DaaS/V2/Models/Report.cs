// -----------------------------------------------------------------------
// <copyright file="Report.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;

namespace DaaS.V2
{
    public class Report
    {
        public string Name { get; set; }
        public string RelativePath { get; set; }
        //[JsonIgnore]
        public string TempPath { get; set; }
        public string FullPermanentStoragePath { get; set; }
    }
}
