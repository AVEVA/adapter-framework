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
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.DataProtectionProvider.Tests;

public class ProtectionProvider_Tests
{
    [Fact]
    public void ProtectionProvider_CreateProtector_Test()
    {
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);

        var protector = protectionProvider.CreateProtector("UnitTests");

        Assert.NotNull(protector);
    }

    [Fact]
    public void ProtectionProvider_CreateProtector_KeyStoreCreated_Test()
    {
        var mockConfigurationProvider = TestUtilities.GetMockConfigurationProvider();
        var secretToProtect = "Hello World!";
        var keystoreDirectoryName = ".DataProtection-Keys";

        var commonApplicationDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            EdgeSystemConstants.AdapterFrameworkDirectoryName, "ProtectorTest", " ").TrimEnd();

        mockConfigurationProvider.Setup(configProvider => configProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(commonApplicationDataPath);

        var protectionProvider = new ProtectionProvider(null, mockConfigurationProvider.Object);

        var protector = protectionProvider.CreateProtector("UnitTests");

        var protectedString = protector.Protect(secretToProtect);

        Assert.NotEqual(secretToProtect, protectedString);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.True(Directory.Exists(Path.Combine(commonApplicationDataPath, keystoreDirectoryName)));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ProtectionProvider_CreateProtector_InvalidInput(string purpose)
    {
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);
        var exceptionThrown = false;

        try
        {
            protectionProvider.CreateProtector(purpose);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public void ProtectionProvider_Protect_Test()
    {
        var stringToProtect = "Secret";
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);

        var protector = protectionProvider.CreateProtector("UnitTests");

        Assert.NotNull(protector);

        var protectedString = protector.Protect(stringToProtect);

        Assert.NotEqual(stringToProtect, protectedString);
    }

    [Fact]
    public void ProtectionProvider_Protect_InvalidInput()
    {
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);

        var protector = protectionProvider.CreateProtector("UnitTests");
        Assert.NotNull(protector);

        Assert.Throws<ArgumentNullException>(() => protector.Protect(null));
    }

    [Fact]
    public void ProtectionProvider_Unprotect_Test()
    {
        var stringToProtect = "Secret";
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);

        var protector = protectionProvider.CreateProtector("UnitTests");

        Assert.NotNull(protector);

        var protectedString = protector.Protect(stringToProtect);

        Assert.NotEqual(stringToProtect, protectedString);

        var unprotectedString = protector.Unprotect(protectedString);

        Assert.Equal(stringToProtect, unprotectedString);
    }

    [Fact]
    public void ProtectionProvider_Unprotect_InvalidInput()
    {
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);

        var protector = protectionProvider.CreateProtector("UnitTests");
        Assert.NotNull(protector);

        Assert.Throws<ArgumentNullException>(() => protector.Unprotect(null));
    }

    [Fact]
    public void ProtectionProvider_Unprotect_CryptoException()
    {
        var protectionProvider = new ProtectionProvider(null, TestUtilities.GetMockConfigurationProvider().Object);

        var protector = protectionProvider.CreateProtector("UnitTests");
        Assert.NotNull(protector);

        Assert.Throws<CryptographicException>(() => protector.Unprotect("PlainText"));
    }
}
