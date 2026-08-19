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
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Utilities;
using Directory = System.IO.Directory;
using IConfigurationProvider = AdapterFramework.Data.Framework.Abstractions.Configuration.IConfigurationProvider;

namespace AdapterFramework.Data.Framework.Host.Helpers;

public static class EdgeSystemHelper
{
    #region Internal Methods

    internal static void ExitWhenBetaTimeoutExpired(DateTime? betaTimeoutDate, ILogger edgeSystemLogger)
    {
        if (BetaTimeout.HasExpiredAndLogTimeoutMessage(betaTimeoutDate, edgeSystemLogger))
        {
            Environment.Exit(0);
        }
    }

    internal static int GetPortNumber(IConfiguration configuration)
    {
        var portNumber = configuration.GetValue<int>(ConfigurationConstants.ApplicationPortKey);

        if (portNumber < ConfigurationConstants.MinPortNumber || portNumber > ConfigurationConstants.MaxPortNumber)
        {
            throw new ArgumentException($"Invalid port number '{portNumber}' specified in appSettings file. Port must be in the range of [{ConfigurationConstants.MinPortNumber},{ConfigurationConstants.MaxPortNumber}].");
        }

        return portNumber;
    }

    internal static void PerformSystemReset(IConfigurationProvider configurationProvider)
    {
        try
        {
            var commonApplicationDataPath = configurationProvider.GetCommonApplicationDataDirectoryPath();

            var directoriesToDelete = Directory.GetDirectories(commonApplicationDataPath)
                .Where(s => !s.EndsWith(EdgeSystemConstants.ConfigurationDirectoryName, StringComparison.InvariantCulture));

            var configurationDirectoryInfo =
                new DirectoryInfo(Path.Combine(commonApplicationDataPath, EdgeSystemConstants.ConfigurationDirectoryName));

            foreach (var configurationFile in configurationDirectoryInfo.GetFiles())
            {
                try
                {
                    File.Delete(configurationFile.FullName);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to delete file {configurationFile.FullName} during a system reset.  Message: {ex.Message}.");
                }
            }

            foreach (var directory in directoriesToDelete)
            {
                try
                {
                    Directory.Delete(directory, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to delete directory {directory} during a system reset.  Message: {ex.Message}.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to perform system level reset. Message: {ex.Message}.");
        }
        finally
        {
            configurationProvider.DeleteConfiguration(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.SystemLevelResetMarkerFile);
        }
    }

    #endregion
}
