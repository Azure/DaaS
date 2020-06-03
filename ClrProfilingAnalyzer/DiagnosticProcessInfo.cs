using Microsoft.Diagnostics.Tracing.Etlx;
using System;

namespace ClrProfilingAnalyzer
{
    class DiagnosticProcessInfo
    {
        public string Name;
        public string SiteName;
        public int Id;        
        public float CPUMSec;
        public bool IsCoreProcess = false;
        const int MAX_PARENT_PROCESS_LEVELS_TO_CHECK = 10;
        public DiagnosticProcessInfo(TraceProcess p, bool isCoreProcess = false)
        {
            Name = p.Name;
            Id = p.ProcessID;
            CPUMSec = p.CPUMSec;
            SiteName = GetSiteName(p);
            IsCoreProcess = isCoreProcess;
        }

        private string GetSiteName(TraceProcess p)
        {
            string sitName = string.Empty;
            try
            {                
                if (p.CommandLine.Contains("w3wp.exe -ap "))
                {
                    SiteName = GetSiteNameFromCmdLine(p.CommandLine);
                }
                else
                {                    
                    var parent = CheckIfParentIsW3wp(p.Parent);
                    if (parent != null)
                    {
                        SiteName = GetSiteNameFromCmdLine(parent.CommandLine);
                    }
                }                
            }
            catch (Exception)
            {
            }
            return SiteName;
        }

        private string GetSiteNameFromCmdLine(string commandLine)
        {
            string siteName = "";
            int startPosition = commandLine.IndexOf("w3wp.exe -ap ") + 14;
            commandLine = commandLine.Substring(startPosition);
            int endPostion = commandLine.IndexOf("\" -v");

            if (endPostion < commandLine.Length)
            {
                siteName = commandLine.Substring(0, endPostion);
            }
            return siteName;
        }

        private TraceProcess CheckIfParentIsW3wp(TraceProcess p)
        {
            int counter = 0;
            TraceProcess parent = null;
            while (p != null && p.ProcessID != 0 && counter < MAX_PARENT_PROCESS_LEVELS_TO_CHECK)
            {
                counter++;
                if (p.Name == "w3wp")
                {
                    parent = p;
                    break;
                }
                if (p.ParentID == 0)
                {
                    break;
                }
            }
            return parent;
        }
    }
}
