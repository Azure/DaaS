//-----------------------------------------------------------------------
// <copyright file="AspNetCoreTraceEvent.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

namespace ClrProfilingAnalyzer
{
    public class AspNetCoreTraceEvent
    {
        public double TimeStampRelativeMSec;
        public string Message;
        public string RelatedActivity;
    }
}
