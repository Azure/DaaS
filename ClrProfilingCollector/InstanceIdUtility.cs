//-----------------------------------------------------------------------
// <copyright file="InstanceIdUtility.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace ClrProflingCollector
{
    public static class InstanceIdUtility
    {
        private static string _instanceId;
        private static string _shortInstanceId;

        public static string GetInstanceId()
        {
            EnsureInstanceId();
            return _instanceId;
        }

        public static string GetShortInstanceId()
        {
            
            EnsureInstanceId();
            return _shortInstanceId;
        }

        private static void EnsureInstanceId()
        {
            if (_instanceId != null)
            {
                return;
            }

            string instanceId = System.Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
            if (String.IsNullOrEmpty(instanceId))
            {
                instanceId = System.Environment.MachineName;
            }
            _instanceId = instanceId;

            _shortInstanceId = _instanceId.Length > 6 ? _instanceId.Substring(0, 6) : _instanceId;
        }
    }
}
