// -----------------------------------------------------------------------
// <copyright file="DiagnoserDetails.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace DaaS.Configuration
{
    public class DiagnoserDetails
    {
        public string Name { get; }
        public List<string> Warnings { get; }
        public string Description { get; }
        public bool RequiresStorageAccount { get; }

        public DiagnoserDetails(Diagnoser diagnoser)
        {
            Name = diagnoser.Name;
            Warnings = new List<string>(diagnoser.GetWarnings());
            Description = diagnoser.Description;
            RequiresStorageAccount = diagnoser.RequiresStorageAccount;
        }
    }
}
