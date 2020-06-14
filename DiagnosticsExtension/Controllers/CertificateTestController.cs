//-----------------------------------------------------------------------
// <copyright file="CertificateTestController.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Web.Http;

namespace DiagnosticsExtension.Controllers
{
    public class CertificateTestController : ApiController
    {
        public class ClientCert
        {
            public string IssuerName;
            public string SubjectName;
            public string Thumbprint;
            public bool IsExportable;
            public bool HasPrivateKey;
            public DateTime ExpirationDate;
        }

        public class CertificateSettings
        {
            public List<ClientCert> Certificates;
            public string AppsettingWebsiteLoadCertificatesValue;
        }

        // GET api/certificatetest
        public CertificateSettings Get()
        {
            CertificateSettings settings = new CertificateSettings();
            X509Store _certStore = null;

            string _strCertificateAppSettings = string.Empty;

            try
            {
                _certStore = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                _certStore.Open(OpenFlags.ReadOnly);
               
                _strCertificateAppSettings = Environment.GetEnvironmentVariable("APPSETTING_WEBSITE_LOAD_CERTIFICATES");

                if (string.IsNullOrEmpty(_strCertificateAppSettings) != true)
                {
                    settings.AppsettingWebsiteLoadCertificatesValue = _strCertificateAppSettings;

                    X509Certificate2Collection col = _certStore.Certificates;

                    if (_certStore.Certificates.Count > 0)
                    {
                        foreach (X509Certificate2 _cert in _certStore.Certificates)
                        {
                            ClientCert cert = new ClientCert
                            {
                                IssuerName = _cert.IssuerName.Name,
                                SubjectName = _cert.SubjectName.Name,
                                Thumbprint = _cert.Thumbprint,
                                HasPrivateKey = _cert.HasPrivateKey,
                                ExpirationDate = _cert.NotAfter,
                                IsExportable = IsPrivateKeyExportable(_cert)
                            };
                            settings.Certificates.Add(cert);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                _certStore.Close();
            }
            return settings;
        }

        private bool IsPrivateKeyExportable(X509Certificate2 col1)
        {
            bool _exportable = false;
            try
            {
                ICspAsymmetricAlgorithm key = (ICspAsymmetricAlgorithm)col1.PrivateKey;
                if (key != null)
                {
                    _exportable = key.CspKeyContainerInfo.Exportable;
                }
            }
            catch { }
            return _exportable;
        }
    }
}
