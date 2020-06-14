//-----------------------------------------------------------------------
// <copyright file="FrebFile.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace DiagnosticsExtension.Parsers
{
    public class LogFile
    {
        public string Content { get; set; }
        public DateTime CreationTimeUtc { get; set; }
        public string FileName { get; set; }
    }
}
