// -----------------------------------------------------------------------
// <copyright file="Session.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DaaS.Sessions
{
    public enum Mode
    {
        Collect,
        CollectAndAnalyze
    }

    public class Session
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Mode Mode { get; set; }
        public string SessionId { get; set; }
        public string Description { get; set; }
        public Status Status { get; set; }
        public DateTime StartTime { get; set; }
        public string Tool { get; set; }
        public string ToolParams { get; set; }
        public List<string> Instances { get; set; }
        public List<ActiveInstance> ActiveInstances { get; set; }
        public DateTime EndTime { get; set; }
        public string BlobStorageHostName { get; set; }
        public string DefaultScmHostName { get; set; }

        [JsonIgnore]
        public string LogsTempDirectory
        {
            get
            {
                return Path.Combine(
                        DaasDirectory.LogsTempDir,
                        SessionId,
                        GetInstanceIdShort());
            }
        }

        internal ActiveInstance GetCurrentInstance()
        {
            if (ActiveInstances != null)
            {
                var currentInstance = ActiveInstances.FirstOrDefault(
                    x => x.Name.Equals(Infrastructure.GetInstanceId(), StringComparison.OrdinalIgnoreCase));
                return currentInstance;
            }

            return null;
        }

        private string GetInstanceIdShort()
        {
            if (GetComputerNameIfExists(out string machineName))
            {
                return machineName;
            }

            return InstanceIdUtility.GetShortInstanceId();
        }

        private bool GetComputerNameIfExists(out string computerName)
        {
            computerName = string.Empty;
            var env = Environment.GetEnvironmentVariable("COMPUTERNAME");
            if (!string.IsNullOrWhiteSpace(env))
            {
                computerName = env;
                return true;
            }

            return false;
        }

    }
}
