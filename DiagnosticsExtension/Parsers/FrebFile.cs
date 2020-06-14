//-----------------------------------------------------------------------
// <copyright file="FrebFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace DiagnosticsExtension
{
    public class FrebFile
    {
        public string FileName { get; set; }
        public string URL { get; set; }
        public string Verb { get; set; }
        public string AppPoolName { get; set; }
        public int StatusCode { get; set; }
        public int TimeTaken { get; set; }
        public string SiteId { get; set; }
        public string Href { get; set; }
        public DateTime DateCreated { get; set; }
    }
}
