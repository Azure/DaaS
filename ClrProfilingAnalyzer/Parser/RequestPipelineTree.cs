//-----------------------------------------------------------------------
// <copyright file="RequestPipelineTree.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace ClrProfilingAnalyzer.Parser
{
    class PipelineNode
    {
        
        public double Duration;         
        public List<PipelineNode> Children;
        public double startTimeRelativeMSec;
        public double endTimeRelativeMSec;
        public string name;

        public PipelineNode(double startTimeRelativeMSec, double endTimeRelativeMSec, string name)
        {
            this.startTimeRelativeMSec = startTimeRelativeMSec;
            this.endTimeRelativeMSec = endTimeRelativeMSec;
            this.name = name;
            this.Duration = endTimeRelativeMSec - startTimeRelativeMSec;
            Children = new List<PipelineNode>();
        }

        public PipelineNode GetCostliestChild(PipelineNode node)
        {
            if (node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    double percentChildDuration = (child.Duration / node.Duration) * 100;
                    if (percentChildDuration > 95)
                    {
                        return GetCostliestChild(child);
                    }
                }
                return node;
            }
            else
            {
                return node;
            }
        }
    }
}
