// -----------------------------------------------------------------------
// <copyright file="TestHelpers.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS;

namespace Daas.Tests
{
    internal static class TestHelpers
    {
        const string temporaryDirectory = "c:\\temp\\daas-testing";
        public static void SetupTestEnvironment()
        {
            Environment.SetEnvironmentVariable("HOME", temporaryDirectory);
            Environment.SetEnvironmentVariable("WEBSITE_COMPUTE_MODE", "Dedicated");
            CleanupTemporaryDirectory();
        }

        private static void CleanupTemporaryDirectory()
        {
            if (Directory.Exists(temporaryDirectory))
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(temporaryDirectory);
                FileSystemHelpers.DeleteDirectorySafe(temporaryDirectory);
            }

            Directory.CreateDirectory(temporaryDirectory);
        }
    }
}
