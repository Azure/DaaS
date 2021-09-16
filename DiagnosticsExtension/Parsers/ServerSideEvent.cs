// -----------------------------------------------------------------------
// <copyright file="ServerSideEvent.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;

namespace DiagnosticsExtension
{
    public class ServerSideEvent
    {
        public DateTime DateAndTime { get; set; }
        public string Source { get; set; }
        public string EventID { get; set; }
        public string TaskCategory { get; set; }
        public string Description { get; set; }
        public string Level { get; set; }
        public string EventRecordID { get; set; }
        public string Computer { get; set; }
    }
}
