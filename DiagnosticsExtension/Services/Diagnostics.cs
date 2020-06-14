//-----------------------------------------------------------------------
// <copyright file="Diagnostics.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MySiteDiagnostics.Diagnostics
{
    public interface IDiagnoser
    {
        string Name { get; }
        void CollectLogs(DateTime utcStartTime, DateTime utcEndTime);
        void CollectLiveDataLogs(TimeSpan timeSpan);
        void Analyze(string logFilePath);
        void Troubleshoot(DateTime utcStartTime, DateTime utcEndTime);
        void TroubleshootLiveData(TimeSpan timeSpan);
    }





    class DiagnoserStub : IDiagnoser
    {
        private string _name = "default";
        public DiagnoserStub(string name)
        {
            _name = name;
        }
        public string Name { 
            get
            {
                return _name;
            }
        }
        public void CollectLogs(DateTime utcStartTime, DateTime utcEndTime)
        {
        }
        public void CollectLiveDataLogs(TimeSpan timeSpan)
        {
        }
        public void Analyze(string logFilePath)
        {
        }
        public void Troubleshoot(DateTime utcStartTime, DateTime utcEndTime)
        {
        }
        public void TroubleshootLiveData(TimeSpan timeSpan)
        {
        }
    }




}
