//-----------------------------------------------------------------------
// <copyright file="HomeController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using DaaS.Sessions;

namespace DiagnosticsExtension.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            SessionController sessionController = new SessionController();
            sessionController.StartSessionRunner();
            return View();
        }
    }
}
