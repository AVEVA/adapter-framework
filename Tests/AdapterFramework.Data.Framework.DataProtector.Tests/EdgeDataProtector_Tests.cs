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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.DataProtectionProvider;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.DataProtector.Tests;

public class EdgeDataProtector_Tests
{
    [Fact]
    public void EdgeDataProtector_Protect_Null()
    {
        var logger = new TestLogger();
        var dataProtectionProvider = CreateProtectionProvider(logger);
        var dataProtector = new EdgeDataProtector(dataProtectionProvider, logger);

        Assert.Throws<ArgumentNullException>(() => dataProtector.Protect(null));
    }

    [Fact]
    public void EdgeDataProtector_Unprotect_Null()
    {
        var logger = new TestLogger();
        var dataProtectionProvider = CreateProtectionProvider(null);
        var dataProtector = new EdgeDataProtector(dataProtectionProvider, logger);

        Assert.Null(dataProtector.Unprotect(null));
    }

    [Fact]
    public void EdgeDataProtector_Protect_ValidInput()
    {
        var superSecureString = "!SuperSecureString!";
        var dataProtectionProvider = CreateProtectionProvider(null);
        var protectedString = new EdgeDataProtector(dataProtectionProvider).Protect(superSecureString);

        Assert.DoesNotMatch(superSecureString, protectedString);
        Assert.True(superSecureString.Length < protectedString.Length);
    }

    [Fact]
    public void EdgeDataProtector_Unprotect_ValidString()
    {
        var superSecureString = "!SuperSecureString!";
        var dataProtectionProvider = CreateProtectionProvider(null);
        var dataProtector = new EdgeDataProtector(dataProtectionProvider);
        var protectedString = dataProtector.Protect(superSecureString);

        Assert.DoesNotMatch(superSecureString, protectedString);

        var unprotectedString = dataProtector.Unprotect(protectedString);

        Assert.Equal(superSecureString, unprotectedString);
    }

    [Fact]
    public void EdgeDataProtector_Unprotect_Fails_NullReturned()
    {
        var plaintTextString = "Hello world";
        var logger = new TestLogger();
        var dataProtectionProvider = CreateProtectionProvider(logger);
        var dataProtector = new EdgeDataProtector(dataProtectionProvider, logger);
        var unprotectedString = dataProtector.Unprotect(plaintTextString);

        Assert.Null(unprotectedString);
        Assert.Single(logger.GetLogMessages());
    }

    private static ProtectionProvider CreateProtectionProvider(ILogger mockLogger)
    {
        var dataProtectionProvider = new ProtectionProvider(mockLogger, TestUtilities.GetMockConfigurationProvider().Object);
        dataProtectionProvider.CreateProtector("Test");
        return dataProtectionProvider;
    }
}
