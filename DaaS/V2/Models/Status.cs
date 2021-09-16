// -----------------------------------------------------------------------
// <copyright file="Status.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DaaS.V2
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Status
    {
        Active,
        Started,
        Complete,
        TimedOut,
        Analyzing
    }
}
