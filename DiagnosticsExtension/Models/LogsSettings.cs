using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DiagnosticsExtension.Models
{
    public class LogsSettings
    {
        public LogsSettings(bool isAspNetCore, bool enabled)
        {
            Stdout = enabled ? LoggingState.Enabled : LoggingState.Disabled;
            IsAspnetCore = isAspNetCore;
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public LoggingState Stdout { get; set; }

        public bool IsAspnetCore { get; set; }
    }

    public enum LoggingState
    {
        Disabled,
        Enabled
    }
}