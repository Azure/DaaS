using DaaS;
using DaaS.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class DaasConfig
    {
        public string Version { get; set; }
        public bool IsDaasRunnerRunning { get; set; }
        public bool DaasWebJobStoppped { get; set; }
        public bool DaasWebJobDisabled { get; set; }
        public DateTime DaasRunnerStartDate { get; set; }
        public string Instance { get; set; }
    }
    public class DaaSVersionController : ApiController
    {
        public DaasConfig Get()
        {
            var daasRunner = System.Diagnostics.Process.GetProcessesByName("daasrunner").FirstOrDefault();
            var config = new DaasConfig
            {
                Version = AssemblyDirectory,
                IsDaasRunnerRunning = daasRunner != null,
                DaasRunnerStartDate = (daasRunner != null) ? daasRunner.StartTime : DateTime.MaxValue,
                Instance = Environment.MachineName,
                DaasWebJobDisabled = CheckWebjobDisabledSetting(),
                DaasWebJobStoppped = CheckWebjobStopped()
            };
            return config;
        }

        private bool CheckWebjobStopped()
        {
            var disabledFileExists = false;
            try
            {
                var destinationDir = @"D:\home\site\Jobs\Continuous\DaaS";
                disabledFileExists = File.Exists(Path.Combine(destinationDir, "disable.job"));
            }
            catch (Exception ex)
            {
                Logger.LogErrorEvent("Failed while checking disable.job file", ex);
            }            

            return disabledFileExists;
        }

        private bool CheckWebjobDisabledSetting()
        {
            bool jobStopped = false;
            var webjobStopped = Environment.GetEnvironmentVariable("WEBJOBS_STOPPED");
            if (webjobStopped !=null)
            {
                jobStopped = webjobStopped == "1";
            }
            return jobStopped;
        }

        //http://stackoverflow.com/questions/52797/how-do-i-get-the-path-of-the-assembly-the-code-is-in
        public static string AssemblyDirectory
        {
            get
            {
                var version = String.Empty;
                bool foundDaasAsPrivateExtension = false;

                if (Directory.Exists(@"D:\home\SiteExtensions\daas"))
                {
                    foundDaasAsPrivateExtension = Directory.Exists(@"D:\home\SiteExtensions\daas\bin");
                    foundDaasAsPrivateExtension = true;
                }

                if (foundDaasAsPrivateExtension)
                {
                    version = AssemblyName.GetAssemblyName(@"D:\home\SiteExtensions\daas\bin\daas.dll").Version.ToString();
                }
                else
                {
                    try
                    {
                        var dir = AppDomain.CurrentDomain.BaseDirectory;
                        //Presuming path format is: C:\Program Files (x86)\SiteExtensions\DaaS\0.7.151022.01
                        version = dir.Split('\\')[4];
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
                return version;
            }
        }
    }
}
