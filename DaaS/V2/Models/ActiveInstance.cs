// -----------------------------------------------------------------------
// <copyright file="ActiveInstance.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace DaaS.V2
{
    public class ActiveInstance
    {
        public string Name { get; set; }
        public List<LogFile> Logs { get; set; } = new List<LogFile>();
        public List<string> CollectorErrors { get; set; } = new List<string>();
        public List<string> AnalyzerErrors { get; set; } = new List<string>();

        public Status Status { get; set; }
        public List<string> CollectorStatusMessages { get; internal set; } = new List<string>();
        public List<string> AnalyzerStatusMessages { get; internal set; } = new List<string>();

        public ActiveInstance()
        {
            Status = Status.Active;
        }

        public ActiveInstance(string instanceName)
        {
            Name = instanceName;
            Status = Status.Active;
        }
    }
}
