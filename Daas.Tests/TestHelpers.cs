using System;
using System.IO;
using DaaS;

namespace Daas.Test
{
    internal static class TestHelpers
    {
        const string TemporaryDirectory = "c:\\temp\\daas-testing";
        public static void SetupTestEnvironment()
        {
            Environment.SetEnvironmentVariable("HOME", TemporaryDirectory);
            Environment.SetEnvironmentVariable("WEBSITE_COMPUTE_MODE", "Dedicated");
            CleanupTemporaryDirectory();
        }

        private static void CleanupTemporaryDirectory()
        {
            if (Directory.Exists(TemporaryDirectory))
            {
                FileSystemHelpers.DeleteDirectoryContentsSafe(TemporaryDirectory);
                FileSystemHelpers.DeleteDirectorySafe(TemporaryDirectory);
            }

            Directory.CreateDirectory(TemporaryDirectory);
        }
    }
}
