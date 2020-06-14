//-----------------------------------------------------------------------
// <copyright file="KeyValuePairModelDescription.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

namespace DiagnosticsExtension.Areas.HelpPage.ModelDescriptions
{
    public class KeyValuePairModelDescription : ModelDescription
    {
        public ModelDescription KeyModelDescription { get; set; }

        public ModelDescription ValueModelDescription { get; set; }
    }
}
