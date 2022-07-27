// -----------------------------------------------------------------------
// <copyright file="CollectorConfiguration.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace DaaS.Configuration
{
    public class CollectorConfiguration
    {
        public string Command { get; set; }
        public string Arguments { get; set; }
        public string PreValidationMethod { get; set; }
        public string PreValidationCommand { get; set; }
        public string PreValidationArguments { get; set; }
    }
}
