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
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Common.Utilities;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.DataProtectionProvider;

public class ProtectionProvider : IDataProtectionProvider
{
    #region Private Constants

    private const string HiddenKeysDirectoryName = ".DataProtection-Keys";
    private const string ChmodCommand = "chmod";
    private const string DataProtectionDirectoryPermissions = "700";
    private const string UnableToCreateKeyStoreMessage = "Unable to create Data Protection KeyStore.";

    #endregion

    #region Private Fields

    private readonly ILogger _logger;
    private readonly IConfigurationProvider _configurationProvider;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Creates <see cref="IDataProtectionProvider"/> instance.
    /// </summary>
    /// <param name="logger">Instance of <see cref="ILogger"/>.</param>
    /// <param name="configurationProvider">Instance of <see cref="IConfigurationProvider"/>.</param>
    public ProtectionProvider(ILogger logger, IConfigurationProvider configurationProvider)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates an <see cref="IDataProtector"/> for given string purpose.
    /// </summary>
    /// <param name="purpose">The purpose used to create the <see cref="IDataProtector"/>.</param>
    /// <returns>An <see cref="IDataProtector"/> tied to the provided purpose.</returns>
    public IDataProtector CreateProtector(string purpose)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(purpose, nameof(purpose));

        IDataProtectionProvider provider;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            provider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(purpose);
        }
        else
        {
            provider = Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create(new DirectoryInfo(GetLinuxKeyStorePath()));

            CreateAndProtectKeyStore();
        }

        return provider.CreateProtector(purpose);
    }

    #endregion

    #region Private Methods

    private string GetLinuxKeyStorePath() => Path.Combine(_configurationProvider.GetCommonApplicationDataDirectoryPath(), HiddenKeysDirectoryName);

    private void CreateAndProtectKeyStore()
    {
        var keyStorePath = GetLinuxKeyStorePath();

        if (Directory.Exists(keyStorePath))
        {
            return;
        }

        CreateKeyStoreDirectory();

        // Shell was not found or chmod command failed to be executed
        if (!ShellCommandExecutor.BashRun(
                $"{ChmodCommand} {DataProtectionDirectoryPermissions} {keyStorePath}",
                out var standardOutput) || !string.IsNullOrEmpty(standardOutput))
        {
            _logger?.LogWarning("Unable to set permissions on DataProtection folder. Output: {StandardOutput}", standardOutput);
        }
    }

    private void CreateKeyStoreDirectory()
    {
        try
        {
            Directory.CreateDirectory(GetLinuxKeyStorePath());
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, UnableToCreateKeyStoreMessage);
            throw;
        }
    }

    #endregion
}
