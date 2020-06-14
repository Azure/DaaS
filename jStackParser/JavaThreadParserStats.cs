//-----------------------------------------------------------------------
// <copyright file="JavaThreadParserStats.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;


namespace jStackParser
{
    class JavaThreadParserStats
    {
        public string StatsType;
        public string InstanceName;
        public string SiteName;        
        public string TraceFileName;
        public Guid ActivityId;        
        public double TimeToParseLogInSeconds;        
    }
}
