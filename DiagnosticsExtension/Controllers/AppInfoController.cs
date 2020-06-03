using DaaS.ApplicationInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class AppInfoController : ApiController
    {
        public AppModelDetectionResult Get()
        {
            AppModelDetector detector = new AppModelDetector();
            var version = detector.Detect(new DirectoryInfo(Path.Combine(Environment.GetEnvironmentVariable("HOME_EXPANDED"), "site", "wwwroot")));
            return version;
        }
    }
}
