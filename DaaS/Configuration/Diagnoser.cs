// -----------------------------------------------------------------------
// <copyright file="Diagnoser.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using DaaS.Configuration;
using DaaS.Diagnostics;

namespace DaaS
{
    public class Diagnoser
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string ProcessCleanupOnCancel { get; set; }
        public bool RequiresStorageAccount { get; set; }
        public CollectorConfiguration Collector { get; set; }
        public AnalyzerConfiguration Analyzer { get; set; }

        public List<string> GetWarnings()
        {
            var warnings = new List<string>();
            var collector = new Collector(this);
            if (!string.IsNullOrWhiteSpace(collector.Warning))
            {
                if (!collector.PreValidationSucceeded(out string additionalInfo))
                {
                    if (!string.IsNullOrWhiteSpace(additionalInfo))
                    {
                        warnings.Add(additionalInfo);
                    }
                    else
                    {
                        warnings.Add(collector.Warning);
                    }
                }
            }

            return warnings;
        }
    }
}
