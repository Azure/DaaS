//-----------------------------------------------------------------------
// <copyright file="IisModuleEvent.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using Microsoft.Diagnostics.Tracing.Parsers;

namespace ClrProfilingAnalyzer
{
    class IisModuleEvent : IisPipelineEvent
    {
        public RequestNotification Notification;
        public bool fIsPostNotification;
        public bool foundEndEvent = false;

        public override string ToString()
        {
            return string.Format("{0} ({1})", Name, Notification.ToString());
        }
    }
}
