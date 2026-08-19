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
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.DataProtector;

public class EdgeDataProtector : IInternalDataProtector
{
    private readonly IDataProtector _dataProtector;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EdgeDataProtector"/> class.
    /// </summary>
    /// <param name="dataProtectionProvider"><see cref="IDataProtectionProvider"/> instance.</param>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    public EdgeDataProtector(IDataProtectionProvider dataProtectionProvider, ILogger logger = null)
    {
        ThrowHelper.ThrowIfArgumentNull(dataProtectionProvider, nameof(dataProtectionProvider));

        _logger = logger;
        _dataProtector = dataProtectionProvider.CreateProtector(nameof(EdgeDataProtector));
    }

    /// <summary>
    /// Cryptographically protects the secret represented as <paramref name="plainText"/>.
    /// </summary>
    /// <param name="plainText">The plainText to protect.</param>
    /// <returns>The protected form of the <see paramref="plainText"/> data.</returns>
    public string Protect(string plainText)
    {
        ThrowHelper.ThrowIfArgumentNull(plainText, nameof(plainText));

        return _dataProtector.Protect(plainText);
    }

    /// <summary>
    /// Cryptographically unprotects the string represented as <paramref name="protectedData"/>.
    /// </summary>
    /// <param name="protectedData">The protected data to unprotect.</param>
    /// <returns>The plaintext form of the <see paramref="protectedData"/> data.</returns>
    public string Unprotect(string protectedData)
    {
        if (string.IsNullOrEmpty(protectedData))
        {
            return protectedData;
        }

        try
        {
            return _dataProtector.Unprotect(protectedData);
        }
        catch (CryptographicException cex)
        {
            _logger.LogError(
                cex, "Unprotect operation has failed. " +
                     "Please make sure that user account under which the System is running" +
                     " is the same as the one used for configuration.");

            return null;
        }
    }
}
