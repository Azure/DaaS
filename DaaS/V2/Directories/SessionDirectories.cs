// -----------------------------------------------------------------------
// <copyright file="SessionDirectories.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;

namespace DaaS.V2
{
    internal class SessionDirectories : DaasDirectory
    {
        private static readonly string sessionsDir = Path.Combine(daasPath, "Sessions");
        internal static string CompletedSessionsDir { get; } = Path.Combine(sessionsDir, "Complete");
        internal static string ActiveSessionsDir { get; } = Path.Combine(sessionsDir, "Active");
    }
}
