//-----------------------------------------------------------------------
// <copyright file="IisRequestInfo.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace ClrProfilingAnalyzer.Parser
{
    public class IisRequestInfo
    {
        public string Method;
        public Guid ContextId;
        public string requestPath;
        public double slowestTime;
        public double totalTimeSpent;
        public IisPipelineEvent slowestPipelineEvent;
        public string csBytes ;
        public string scBytes ;
        public string statusCode;
        public string SubStatusCode;
        public RequestFailureDetails FailureDetails;        
        public Guid RelatedActivityId;
        public bool HasActivityStack;
        public bool HasThreadStack;
    }
}
