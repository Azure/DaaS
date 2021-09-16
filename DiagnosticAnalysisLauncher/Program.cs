// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiagnosticAnalysisLauncher
{
    class Program
    {
        static void Main(string[] args)
        {
            var launcher = new DiagnosticAnalysisLauncher(args[0]);
            launcher.AnalyzeDump();
        }
    }
}
