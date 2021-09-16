// -----------------------------------------------------------------------
// <copyright file="TaskAndCancellationTokenV2.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace DaaSRunner
{
    internal class TaskAndCancellationTokenV2
    {
        internal Task UnderlyingTask { get; set; }
        internal CancellationTokenSource CancellationTokenSource { get; set; }
    }
}