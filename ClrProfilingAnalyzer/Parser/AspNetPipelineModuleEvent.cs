//-----------------------------------------------------------------------
// <copyright file="AspNetPipelineModuleEvent.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace ClrProfilingAnalyzer.Parser
{
    class AspNetPipelineModuleEvent : IisPipelineEvent
    {
        public string ModuleName;
        public bool foundEndEvent = false;

        public override string ToString()
        {
            return ModuleName;
        }
    }
}
