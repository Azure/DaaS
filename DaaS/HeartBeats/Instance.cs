//-----------------------------------------------------------------------
// <copyright file="Instance.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DaaS.Configuration;

namespace DaaS.HeartBeats
{
    [Serializable]
    public class Instance
    {

        public string Name { get; set; }

        public Instance(string instanceName)
        {
            Name = instanceName;
        }

        public Instance() { }

        public override string ToString()
        {
            return Name;
        }

        public static Instance GetCurrentInstance()
        {
            return GetInstanceWithName(Settings.InstanceName);
        }

        public static Instance GetInstanceWithName(string instanceName)
        {
            var instance = new Instance()
            {
                Name = instanceName
            };

            return instance;
        }

        public override bool Equals(object obj)
        {
            Instance otherInstance = obj as Instance;
            if (otherInstance == null)
            {
                return false;
            }

            return this.Equals(otherInstance);
        }

        protected bool Equals(Instance other)
        {
            return string.Equals(Name, other.Name);
        }

        public override int GetHashCode()
        {
            return (Name != null ? Name.GetHashCode() : 0);
        }
    }
}
