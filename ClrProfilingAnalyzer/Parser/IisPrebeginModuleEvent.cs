//-----------------------------------------------------------------------
// <copyright file="IisPrebeginModuleEvent.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

namespace ClrProfilingAnalyzer
{
    class IisPrebeginModuleEvent : IisPipelineEvent
    {
        public override string ToString()
        {
            return Name + " (PreBegin)";
        }
    }
}
