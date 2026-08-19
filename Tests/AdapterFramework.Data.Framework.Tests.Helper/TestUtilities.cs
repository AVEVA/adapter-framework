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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Management;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Common.Security;
using AdapterFramework.Data.Framework.DataProtectionProvider;
using AdapterFramework.Data.Framework.DataProtector;

namespace AdapterFramework.Data.Framework.Tests.Helper;

public static class TestUtilities
{
    public static IInternalDataProtector CreateDataProtectorInstance(ILogger logger, IConfigurationProvider configurationProvider)
    {
        configurationProvider ??= GetMockConfigurationProvider().Object;

        var dataProtectionProvider = new ProtectionProvider(logger, configurationProvider);
        return new EdgeDataProtector(dataProtectionProvider, logger);
    }

    public static SecretsManager CreateSecretsManagerInstance(IInternalDataProtector protector, ILogger logger, IConfigurationProvider configurationProvider,
        IRuntimeManagementRegistry runtimeManagementRegistry = null)
    {
        configurationProvider ??= GetMockConfigurationProvider().Object;
        protector ??= CreateDataProtectorInstance(logger, configurationProvider);
        runtimeManagementRegistry ??= new Mock<IRuntimeManagementRegistry>().Object;

        return new SecretsManager(protector, logger, configurationProvider, runtimeManagementRegistry);
    }

    public static IConfigurationProtector CreateConfigurationProtector(ILogger logger, ISecretsManager secretsManager = null, IInternalDataProtector dataProtector = null)
    {
        dataProtector ??= CreateDataProtectorInstance(logger, null);
        secretsManager ??= CreateSecretsManagerInstance(dataProtector, logger, null);

        return new ConfigurationProtector(secretsManager);
    }

    public static object GetFieldValueFromObject<T>(string fieldName, T instance) => typeof(T).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

    public static List<byte[]> GenerateRandomBytes(int numMessages, int msgByteSize)
    {
        var msgList = new List<byte[]>();

        var rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgByteSize];
            rnd.NextBytes(item);

            msgList.Add(item);
        }

        return msgList;
    }

    public static Mock<IConfigurationProvider> GetMockConfigurationProvider()
    {
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        mockConfigurationProvider.Setup(configProvider => configProvider.GetCommonApplicationDataDirectoryPath(null))
            .Returns(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                EdgeSystemConstants.AdapterFrameworkDirectoryName, "UnitTests", " ").TrimEnd());

        return mockConfigurationProvider;
    }

    public static string GenerateRandomString(int length)
    {
        if (length < 0)
        {
            return null;
        }

        if (length == 0)
        {
            return string.Empty;
        }
        
        const string Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var stringChars = new char[length];
        var random = new Random();

        for (var i = 0; i < stringChars.Length; i++)
        {
            stringChars[i] = Characters[random.Next(Characters.Length)];
        }

        return new string(stringChars);
    }

    public static void CleanupDirectories(string pathToCleanup)
    {
        if (Directory.Exists(pathToCleanup))
        {
            Directory.Delete(pathToCleanup, true);
        }
    }
}
