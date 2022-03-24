// -----------------------------------------------------------------------
// <copyright file="ManagedIdentityCredentialTokenValidator.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Web;
using Azure.Core;
using Azure.Identity;
using DiagnosticsExtension.Models.ConnectionStringValidator.Exceptions;

namespace DiagnosticsExtension.Models.ConnectionStringValidator
{
    public static class ManagedIdentityCredentialTokenValidator
    {
        public static ManagedIdentityCredential GetValidatedCredential(string clientId)
        {
            var tokenCredential = new ManagedIdentityCredential(clientId);

            var accessToken = tokenCredential.GetTokenAsync(new TokenRequestContext(scopes: new string[] { "https://storage.azure.com/.default" }) { });

            JwtSecurityTokenHandler jwtHandler = new JwtSecurityTokenHandler();

            string appId = jwtHandler.ReadJwtToken(accessToken.Result.Token).Claims.ToList().Where(c => c.Type == "appid").FirstOrDefault().Value;

            if(clientId != "" && appId != clientId)
            {
                throw new ManagedIdentityException(Constants.ClientIdInvalidTokenGeneratedResponse);
            }

            return tokenCredential;
        }

    }
    
}
