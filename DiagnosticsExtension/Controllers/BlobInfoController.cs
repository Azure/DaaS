//-----------------------------------------------------------------------
// <copyright file="BlobInfoController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using DaaS.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class BlobInfoController : ApiController
    {
        // GET api/blobinfo
        public string Get()
        {
            var retVal = "NotConfigured";
            try
            {
                var blobSasUri = DaaS.Configuration.Settings.Instance.BlobStorageSas;
                if (!string.IsNullOrWhiteSpace(blobSasUri))
                {
                    retVal = blobSasUri;
                }
            }
            catch (Exception ex)
            {
                DaaS.Logger.LogErrorEvent("Error retrieving blob info", ex);
                retVal = "Error";
            }

            return retVal;
        }
    }
}
