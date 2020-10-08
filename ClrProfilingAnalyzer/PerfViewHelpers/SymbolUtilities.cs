//-----------------------------------------------------------------------
// <copyright file="SymbolUtilities.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Symbols;
using System.Reflection;
using System.Diagnostics;
using DaaS;

namespace ClrProfilingAnalyzer
{
    static class SymbolUtilities
    {
        public static SymbolReader GetSymbolReader(string additionalPath, string etlFilePath = null, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            string localSymbolPath = DaaS.EnvironmentVariables.DaasSymbolsPath;
            if (!Directory.Exists(DaaS.EnvironmentVariables.DaasPath))
            {
                localSymbolPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "symbols");
            }
            SymbolPath symPath = new SymbolPath(string.Format(@"srv*{0}*http://msdl.microsoft.com/download/symbols", localSymbolPath));
            if ((symbolFlags & SymbolReaderOptions.CacheOnly) != 0)
                symPath = new SymbolPath("SRV*" + symPath.DefaultSymbolCache());

            var assemblyDir = Assembly.GetEntryAssembly().Location;

            // 
            // This is required if we ever don't end up caching the symbols of
            // our images on the symbol server. In those cases, the DAAS package
            // should be updated with the Symbols folder for ngen binaries.
            // 
            var daasPath = assemblyDir.Substring(0, assemblyDir.IndexOf("bin", StringComparison.InvariantCultureIgnoreCase));
            var daasSymbolPath = Path.Combine(daasPath, "Symbols");
            if (!Directory.Exists(daasSymbolPath))
            {
                daasSymbolPath = "";
            }

            symPath.Insert($"srv*{EnvironmentVariables.NdpCorePdbPath}*");
            symPath.Insert($@"srv*{EnvironmentVariables.DaasSymbolsPath}*" + daasSymbolPath);

            string localSymDir = symPath.DefaultSymbolCache();
            if (etlFilePath != null)
            {
                // Add the directory where the file resides and a 'symbols' subdirectory 
                var filePathDir = Path.GetDirectoryName(etlFilePath);
                if (filePathDir.Length != 0)
                {
                    // Then the directory where the .ETL file lives. 
                    symPath.Insert(filePathDir);

                    // If there is a 'symbols' directory next to the data file, look for symbols there
                    // as well.   Note that we also put copies of any symbols here as well (see below)
                    string potentiallocalSymDir = Path.Combine(filePathDir, "symbols");
                    if (Directory.Exists(potentiallocalSymDir))
                    {
                        symPath.Insert(potentiallocalSymDir);
                        symPath.Insert("SRV*" + potentiallocalSymDir);
                        localSymDir = potentiallocalSymDir;
                    }

                    // WPR conventions add any .etl.ngenPDB directory to the path too.   has higher priority still. 
                    var wprSymDir = etlFilePath + ".NGENPDB";
                    if (Directory.Exists(wprSymDir))
                        symPath.Insert("SRV*" + wprSymDir);
                    else
                    {
                        // I have now seen both conventions .etl.ngenpdb and .ngenpdb, so look for both.  
                        wprSymDir = Path.ChangeExtension(etlFilePath, ".NGENPDB");
                        if (Directory.Exists(wprSymDir))
                            symPath.Insert("SRV*" + wprSymDir);
                    }
                    // VS uses .NGENPDBS as a convention.  
                    wprSymDir = etlFilePath + ".NGENPDBS";
                    if (Directory.Exists(wprSymDir))
                        symPath.Insert("SRV*" + wprSymDir);

                    if (Directory.Exists(additionalPath))
                    {
                        symPath.Insert(additionalPath);
                    }
                }
            }
            DaaS.Logger.LogInfo("Symbol reader _NT_SYMBOL_PATH=");
            foreach (var element in symPath.Elements)
                DaaS.Logger.LogInfo($"    {element};");

            TextWriter textWriter = null;

            if (Trace.Listeners["TextWriterTraceListener"] !=null)
            {
                var textWriterListener = Trace.Listeners["TextWriterTraceListener"] as System.Diagnostics.TextWriterTraceListener;
                textWriter = textWriterListener.Writer;
            }
            else
            {
                textWriter = Console.Out;
            }
            
            SymbolReader ret = new SymbolReader(textWriter, symPath.ToString())
            {
                Options = symbolFlags
            };

            ret.SecurityCheck = (pdbFile => true);

            if (localSymDir != null)
                ret.OnSymbolFileFound += (pdbPath, pdbGuid, pdbAge) => CacheInLocalSymDir(localSymDir, pdbPath, pdbGuid, pdbAge, Console.Out);

            return ret;
        }

        private static void CacheInLocalSymDir(string localPdbDir, string pdbPath, Guid pdbGuid, int pdbAge, TextWriter log)
        {
            // We do this all in a fire-and-forget task so that it does not block the User.   It is 
            // optional after all.  
            Task.Factory.StartNew(delegate ()
            {
                try
                {
                    var fileName = Path.GetFileName(pdbPath);
                    if (pdbGuid != Guid.Empty)
                    {
                        var pdbPathPrefix = Path.Combine(localPdbDir, fileName);
                        // There is a non-trivial possibility that someone puts a FILE that is named what we want the dir to be.  
                        if (File.Exists(pdbPathPrefix))
                        {
                            // If the pdb path happens to be the SymbolCacheDir (a definite possibility) then we would
                            // clobber the source file in our attempt to set up the target.  In this case just give up
                            // and leave the file as it was.  
                            if (string.Compare(pdbPath, pdbPathPrefix, StringComparison.OrdinalIgnoreCase) == 0)
                                return;
                            DaaS.Logger.LogInfo($"Removing file {pdbPathPrefix} from symbol cache to make way for symsrv files.");
                            File.Delete(pdbPathPrefix);
                        }
                        localPdbDir = Path.Combine(pdbPathPrefix, pdbGuid.ToString("N") + pdbAge.ToString());
                    }

                    if (!Directory.Exists(localPdbDir))
                        Directory.CreateDirectory(localPdbDir);

                    var localPdbPath = Path.Combine(localPdbDir, fileName);
                    var fileExists = File.Exists(localPdbPath);
                    if (!fileExists || File.GetLastWriteTimeUtc(localPdbPath) != File.GetLastWriteTimeUtc(pdbPath))
                    {
                        if (fileExists)
                            DaaS.Logger.LogInfo($"WARNING: overwriting existing file {localPdbPath}.");

                        DaaS.Logger.LogInfo($"Copying {pdbPath} to local cache {localPdbPath}");
                        // Do it as a copy and a move so that the update is atomic.  
                        var newLocalPdbPath = localPdbPath + ".new";
                        FileUtilities.ForceCopy(pdbPath, newLocalPdbPath);
                        FileUtilities.ForceMove(newLocalPdbPath, localPdbPath);
                    }
                }
                catch (Exception e)
                {
                    DaaS.Logger.LogInfo($"Error trying to update local PDB cache {e.Message}");
                }
            });
        }

    }
}
