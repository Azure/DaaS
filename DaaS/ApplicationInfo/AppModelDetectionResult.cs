using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DaaS.ApplicationInfo
{
    public class AppModelDetectionResult
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public RuntimeFramework? Framework { get; set; }
        public string FrameworkVersion { get; set; }
        public string AspNetCoreVersion { get; set; }
        public string CoreProcessName { get; set; }
        public string LoggingLevel { get; set; }
        public List<WebConfigSection> WebConfigSections { get; set; }
    }
}
