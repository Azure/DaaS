using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;

namespace jStackParser
{
    class Program
    {
        static string m_OutputPath = "";
        static string m_OutputPathWithInstanceName = "";
        static string m_InstanceName = "";
        static string m_JavaProcessId = "";
        static string m_jStackLog = "";

        static void Main(string[] args)
        {
            m_jStackLog = args[0];
            m_OutputPath = args[1];

            if (!IsValidFile())
            {
                return;
            }

            DaaS.Logger.Init(m_jStackLog, m_OutputPath, "jStackAnalyzer", false);

            JavaThreadParserStats stats = new JavaThreadParserStats
            {
                StatsType = "JavaThreadParserStats",
                ActivityId = DaaS.Logger.ActivityId,
                SiteName = DaaS.Logger.SiteName,
                TraceFileName = m_jStackLog,

            };

            try
            {
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                DaaS.Logger.LogDiagnoserEvent($"Opening JStack LogFile {m_jStackLog} and outputPath is { m_OutputPath}");


                m_InstanceName = GetMachineName(m_jStackLog);
                stats.InstanceName = m_InstanceName;

                DaaS.Logger.LogDiagnoserVerboseEvent($"Instance name is { m_InstanceName}");

                m_OutputPathWithInstanceName = Path.Combine(m_OutputPath, m_InstanceName);

                Directory.CreateDirectory(Path.Combine(m_OutputPathWithInstanceName, "reportdata"));

                CopyStaticContent(m_OutputPath);

                var stackDump = JavaThread.ParseJstackLog(m_jStackLog);
                stopWatch.Stop();
                stats.TimeToParseLogInSeconds = stopWatch.Elapsed.TotalSeconds;

                stackDump.FileName = Path.GetFileName(m_jStackLog);
                stackDump.Timestamp = DateTime.Now.ToString("u");
                stackDump.SiteName = DaaS.Logger.SiteName;
                stackDump.FullFilePath = m_jStackLog;
                stackDump.MachineName = m_InstanceName;

                using (StreamWriter file = File.CreateText(Path.Combine(m_OutputPathWithInstanceName, "reportdata", "stackDump.json")))
                {
                    var jsonWriter = new JsonTextWriter(file) { Formatting = Formatting.Indented };
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(jsonWriter, stackDump);
                }
               
                DaaS.Logger.TraceStats(JsonConvert.SerializeObject(stats));
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogDiagnoserErrorEvent("Failed while analyzing the jstacklog", ex);
                DaaS.Logger.TraceFatal($"Failed while analyzing the trace with exception - {ex.GetType().ToString()}: {ex.Message}", false);
            }

        }

        private static string GetMachineName(string jStackLog)
        {
            string machineName = Path.GetFileNameWithoutExtension(jStackLog);

            var fileNameArray = machineName.Split('_');
            if (fileNameArray.Length > 1)
            {
                machineName = fileNameArray[0];
                m_JavaProcessId = fileNameArray[1];
            }
            else
            {
                throw new ApplicationException("Failed to parse instance name and Process Id from logfile");
            }

            DaaS.Logger.LogInfo($"GetMachineName returning {machineName}");
            return machineName;           
        }

        internal static void CopyStaticContent(string outputReportPath)
        {
            string outputPathWithInstance = Path.Combine(outputReportPath, m_InstanceName);
            string staticContentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "staticcontent");

            foreach (var dirPath in Directory.GetDirectories(staticContentPath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(staticContentPath, outputPathWithInstance));
            }

            foreach (var newPath in Directory.GetFiles(staticContentPath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(staticContentPath, outputPathWithInstance), true);
            }

            string redirectUrl = m_InstanceName + "/index.html";
            string placeHolderhtml = $@"<!DOCTYPE HTML>
                                        <html lang='en - US'>
                                              <head>
                                                  <meta charset = 'UTF-8'>
                                                   <meta http - equiv = 'refresh' content = '1; url={redirectUrl}' >
                                                        <script type = 'text/javascript' >
                                                             window.location.href = '{redirectUrl}'
                                                         </script >
                                                         <title > Page Redirection </title >
                                                        </head>
                                                        <body>                          
                                                If you are not redirected automatically, follow this < a href = '{redirectUrl}' > link to example</a>.
                                                 </body>
                                             </html>";

            string placeHolderFileName = Path.Combine(outputReportPath, $"{m_InstanceName}_jstack_{m_JavaProcessId}.html");
            File.WriteAllText(placeHolderFileName, placeHolderhtml);
        }
        private static bool IsValidFile()
        {
           
                // TODO: This is Required so we dont end up analyzing the .Diaglog
                // Once we ensure that DaaS is coded to ignore files of certain extensions, we will
                // remove this logic. Right now it is required to keept DaaS happy !!!
                if (!m_jStackLog.EndsWith(".log"))
                {
                    Console.WriteLine($"Ignoring file {m_jStackLog} as it is not a jstacklog file");
                    string logsDir = Path.Combine(m_OutputPath, "logs");
                    if (!Directory.Exists(logsDir))
                    {
                        Directory.CreateDirectory(logsDir);
                    }

                    using (StreamWriter file = File.CreateText(Path.Combine(logsDir, $"NoOp_{Path.GetFileName(m_jStackLog).Replace("_jStackParser", "")}.log")))
                    {
                        file.WriteLine($"Ignoring file {m_jStackLog} as it is not a jstacklog file");
                    }
                    return false;
                }
                return true;
            
        }
    }
}
