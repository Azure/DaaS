//-----------------------------------------------------------------------
// <copyright file="IisPipelineEvent.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

namespace ClrProfilingAnalyzer
{
    public class IisPipelineEvent
    {
        public string Name;
        public int ProcessId;
        public int StartThreadId;
        public int EndThreadId;
        public double StartTimeRelativeMSec = 0;
        public double EndTimeRelativeMSec = 0;
        public int ChildRequestRecurseLevel = 0;
        public override string ToString()
        {
            return Name;
        }
    }
}
