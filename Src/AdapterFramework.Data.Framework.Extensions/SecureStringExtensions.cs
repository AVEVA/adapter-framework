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
using System.Runtime.InteropServices;
using System.Security;

namespace AdapterFramework.Data.Framework.Extensions;

/// <summary>
/// Provides extensions to secure string.
/// </summary>
public static class SecureStringExtensions
{
    /// <summary>
    /// Converts <see cref="SecureString"/> to a <see cref="string"/>.
    /// </summary>
    /// <param name="secureString">Secure string to convert.</param>
    /// <returns>String content of <paramref name="secureString"/>.</returns>
    public static string ToUnsecureString(this SecureString secureString)
    {
        ThrowHelper.ThrowIfArgumentNull(secureString, nameof(secureString));

        var unmanagedString = IntPtr.Zero;

        try
        {
            unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(secureString);

            return Marshal.PtrToStringUni(unmanagedString);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
        }
    }

    /// <summary>
    /// Converts <see cref="string"/> into a <see cref="SecureString"/>.
    /// </summary>
    /// <param name="secret">Plain string to convert.</param>
    /// <returns>Secure string object containing <paramref name="secret"/> content.</returns>
    public static SecureString ToSecureString(this string secret)
    {
        ThrowHelper.ThrowIfArgumentNull(secret, nameof(secret));

        unsafe
        {
            fixed (char* plainStringChars = secret)
            {
                var secureString = new SecureString(plainStringChars, secret.Length);
                secureString.MakeReadOnly();

                return secureString;
            }
        }
    }
}
