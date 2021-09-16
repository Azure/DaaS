// -----------------------------------------------------------------------
// <copyright file="Program.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace DiagnosticAnalysisLauncher
{
    class Program
    {
        static void Main(string[] args)
        {
            string outputFolder = "";
            if (args.Length > 1)
            {
                outputFolder = args[1];
            }

            var launcher = new DiagnosticAnalysisLauncher(args[0], outputFolder);
            launcher.AnalyzeDump();
        }
    }
}
