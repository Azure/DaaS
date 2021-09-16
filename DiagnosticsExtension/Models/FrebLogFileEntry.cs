// -----------------------------------------------------------------------
// <copyright file="FrebLogFileEntry.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DiagnosticsExtension
{
    class FrebLogFileEntry
    {
        public string Name { get; set; }
        public int Size { get; set; }
        public DateTime Mtime { get; set; }
        public string Mime { get; set; }
        public string Href { get; set; }
        public string Path { get; set; }
    }
}
