//-----------------------------------------------------------------------
// <copyright file="Paths.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Sessions;

namespace DaaS
{
    class Paths
    {
        public static string GetRelativeSessionDir(SessionStatus sessionStatus)
        {
            switch (sessionStatus)
            {
                case SessionStatus.Complete:
                case SessionStatus.Cancelled:
                case SessionStatus.Error:
                    return SessionDirectories.CompletedSessionsDir;
                case SessionStatus.CollectedLogsOnly:
                    return SessionDirectories.CollectedLogsOnlySessionsDir;
                case SessionStatus.Active:
                    return SessionDirectories.ActiveSessionsDir;
                default:
                    throw new Exception("Umm...I don't recognize this session status");
            }
        }
    }
}
