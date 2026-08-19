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
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Configuration;
using AdapterFramework.Data.Framework.Host.Configuration;

namespace AdapterFramework.Data.Framework.Host.Helpers;

internal class CommandLineArgumentHelper
{
    internal static IConfiguration CreateConfiguration(string[] arguments, out string errorMessage)
    {
        var configurationBuilder = new ConfigurationBuilder();
        var baseDirectoryName = Path.GetDirectoryName(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(baseDirectoryName))
        {
            errorMessage = $"{nameof(AppContext.BaseDirectory)} is null or empty.";
            return null;
        }

        configurationBuilder.SetBasePath(baseDirectoryName);
        configurationBuilder.AddJsonFile(GetAppSettingsFileName(arguments), optional: false, reloadOnChange: false);

        var configurationValueOverride = new Dictionary<string, string>();
        foreach (var argument in arguments)
        {
            if (argument.Contains(ConfigurationConstants.ApplicationDataDirectoryParameter, StringComparison.InvariantCultureIgnoreCase))
            {
                if (!TryGetStringArgumentValue(argument, ConfigurationConstants.ApplicationDataDirectoryParameter, out var applicationDataDirectory, out errorMessage))
                {
                    return null;
                }

                configurationValueOverride[ConfigurationConstants.ApplicationDataDirectoryKey] = applicationDataDirectory;
            }

            if (argument.Contains(ConfigurationConstants.ApplicationPortParameter, StringComparison.InvariantCultureIgnoreCase))
            {
                var applicationPortString = argument[ConfigurationConstants.ApplicationPortParameter.Length..].Trim();
                if (!int.TryParse(applicationPortString, out var applicationPort) ||
                    applicationPort < ConfigurationConstants.MinPortNumber ||
                    applicationPort > ConfigurationConstants.MaxPortNumber)
                {
                    errorMessage = $"Invalid value \"{applicationPortString}\" provided for startup argument {ConfigurationConstants.ApplicationPortParameter}.";
                    return null;
                }

                configurationValueOverride[ConfigurationConstants.ApplicationPortKey] = applicationPortString;
            }

            if (argument.Contains(ConfigurationConstants.DeviceNameParameter, StringComparison.InvariantCultureIgnoreCase))
            {
                if (!TryGetStringArgumentValue(argument, ConfigurationConstants.DeviceNameParameter, out var deviceName, out errorMessage))
                {
                    return null;
                }

                configurationValueOverride[ConfigurationConstants.DeviceNameKey] = deviceName;
            }
        }

        configurationBuilder.AddInMemoryCollection(configurationValueOverride);
        errorMessage = null;
        return configurationBuilder.Build();
    }

    private static bool TryGetStringArgumentValue(string inputArgument, string argumentName, out string argumentValue, out string errorMessage)
    {
        errorMessage = null;
        argumentValue = inputArgument[argumentName.Length..].Trim();
        if (string.IsNullOrWhiteSpace(argumentValue))
        {
            errorMessage = $"Invalid input provided for startup argument {argumentName}.";
            return false;
        }

        return true;
    }

    private static string GetAppSettingsFileName(string[] arguments)
    {
        var appSettingsFile = ConfigurationConstants.DefaultAppSettingsFileName;

        foreach (var argument in arguments)
        {
            if (argument.Contains(ConfigurationConstants.InstanceParameter, StringComparison.InvariantCultureIgnoreCase))
            {
                var instanceId = argument[ConfigurationConstants.InstanceParameter.Length..].TrimEnd();
                if (!instanceId.Equals(ConfigurationConstants.DefaultInstanceId, StringComparison.InvariantCultureIgnoreCase))
                {
                    appSettingsFile = string.Format(CultureInfo.InvariantCulture, ConfigurationConstants.AppSettingsFileTemplate, instanceId);
                }

                break;
            }
        }

        return appSettingsFile;
    }
}
