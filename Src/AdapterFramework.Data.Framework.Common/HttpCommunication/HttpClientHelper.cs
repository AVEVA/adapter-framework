// Copyright 2018-2026 AVEVA Group Limited
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace AdapterFramework.Data.Framework.Common.HttpCommunication;

public static class HttpClientHelper
{
    private static readonly TimeSpan _pooledConnectionLifetime = TimeSpan.FromMinutes(30);

    public static SocketsHttpHandler GetCommonSocketHttpHandler(bool warningPrinted, Action<DateTime, string> logAction, bool validateServerCertificate = true)
    {
        var sslOptions = validateServerCertificate
            ? GetCommonSslClientAuthenticationOptions(warningPrinted, logAction)
            : GetSslClientAuthenticationOptionsWithoutCertValidation();

        var httpClientHandler = new SocketsHttpHandler
        {
            SslOptions = sslOptions,
            PooledConnectionLifetime = _pooledConnectionLifetime,
            Credentials = null,
        };

        return httpClientHandler;
    }

    private static SslClientAuthenticationOptions GetCommonSslClientAuthenticationOptions(bool warningPrinted, Action<DateTime, string> logAction)
    {
        return new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslErrors) =>
            {
                // ignore if sslErrors only contains certificate expiration error
                if (sslErrors == SslPolicyErrors.RemoteCertificateChainErrors
                        && certificate != null && chain != null && chain.ChainStatus.Length == 1
                        && chain.ChainStatus[0].Status == X509ChainStatusFlags.NotTimeValid)
                {
                    if (!warningPrinted)
                    {
                        logAction.Invoke(((X509Certificate2)certificate).NotAfter, certificate.Subject);
                        warningPrinted = true;
                    }

                    return true;
                }
                else
                {
                    warningPrinted = false;
                    return sslErrors == SslPolicyErrors.None;
                }
            },
        };
    }

    private static SslClientAuthenticationOptions GetSslClientAuthenticationOptionsWithoutCertValidation()
    {
#pragma warning disable CA5359 // Do Not Disable Certificate Validation
        return new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, _, _, _) => true,
        };
#pragma warning restore CA5359 // Do Not Disable Certificate Validation
    }
}
