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
using Microsoft.Extensions.Configuration;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Helpers;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace AdapterFramework.Data.Framework.Host.Tests.Helpers;

public class CommandLineArgumentHelper_Tests : IDisposable
{
    private const string AppDataDirectoryKeyword = "--appLicationdatadirectory:";
    private const string PortKeyword = "--port:";
    private const string DeviceNameKeyword = "--deviceName:";
    private const string TestAppDataDirectory = "test/";
    private const string TestMachineName = "TestMachine";

    private readonly TextWriter _defaultTextWriter;
    private bool _disposed;

    public CommandLineArgumentHelper_Tests()
    {
        _defaultTextWriter = Console.Out;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("", "1234", TestMachineName)]
    [InlineData(TestAppDataDirectory, "", TestMachineName)]
    [InlineData(TestAppDataDirectory, "abcd", TestMachineName)]
    [InlineData(TestAppDataDirectory, "0", TestMachineName)]
    [InlineData(TestAppDataDirectory, "65535", TestMachineName)]
    [InlineData(TestAppDataDirectory, "65535", "")]
    [InlineData(TestAppDataDirectory, "65535", "  ")]
    internal void CreateConfiguration_InvalidArguments_Test(string dataDirectoryArgument, string portArgument, string machineNameArgument)
    {
        var argumentList = new List<string>();
        argumentList.Add($"{AppDataDirectoryKeyword}{dataDirectoryArgument}");
        argumentList.Add($"{PortKeyword}{portArgument}");
        argumentList.Add($"{DeviceNameKeyword}{machineNameArgument}");
        var config = CommandLineArgumentHelper.CreateConfiguration(argumentList.ToArray(), out var error);
        Assert.NotEmpty(error);
        Assert.Null(config);
    }

    [Theory]
    [InlineData(TestAppDataDirectory, "1234", TestMachineName)]
    [InlineData(null, "1234", null)]
    [InlineData(TestAppDataDirectory, null, null)]
    [InlineData(null, null, null)]
    [InlineData(null, null, TestMachineName)]
    internal void CreateConfiguration_ValidArguments_Test(string dataDirectoryArgument, string portArgument, string machineNameArgument)
    {
        var appSettingsFilePath = AppContext.BaseDirectory + "appsettings.json";
        try
        {
            var portFromFile = "4567";
            var dataDirectoryFromFile = "Adapters/Platform";
            var appSettingsFileContent =
            "{" +
              "\"ApplicationSettings\": {" +
                 $"\"Port\": {portFromFile}," +
                 $"\"ApplicationDataDirectory\": \"{dataDirectoryFromFile}\"" +
              "}" +
            "}";

            File.WriteAllText(appSettingsFilePath, appSettingsFileContent);

            var argumentList = new List<string>();
            if (dataDirectoryArgument != null)
            {
                argumentList.Add($"{AppDataDirectoryKeyword.ToUpperInvariant()}{dataDirectoryArgument}");
            }

            if (portArgument != null)
            {
                argumentList.Add($"{PortKeyword}{portArgument}");
            }

            if (machineNameArgument != null)
            {
                argumentList.Add($"{DeviceNameKeyword.ToUpperInvariant()}{machineNameArgument}");
            }

            var config = CommandLineArgumentHelper.CreateConfiguration(argumentList.ToArray(), out var error);
            Assert.Null(error);
            Assert.NotNull(config);

            if (dataDirectoryArgument != null)
            {
                Assert.Equal(dataDirectoryArgument, config.GetValue<string>(ConfigurationConstants.ApplicationDataDirectoryKey));
            }
            else
            {
                Assert.Equal(dataDirectoryFromFile, config.GetValue<string>(ConfigurationConstants.ApplicationDataDirectoryKey));
            }

            if (portArgument != null)
            {
                Assert.Equal(portArgument, config.GetValue<string>(ConfigurationConstants.ApplicationPortKey));
            }
            else
            {
                Assert.Equal(portFromFile, config.GetValue<string>(ConfigurationConstants.ApplicationPortKey));
            }

            Assert.Equal(machineNameArgument, config.GetValue<string>(ConfigurationConstants.DeviceNameKey));
        }
        finally
        {
            if (File.Exists(appSettingsFilePath))
            {
                File.Delete(appSettingsFilePath);
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Console.SetOut(_defaultTextWriter);
        }

        _disposed = true;
    }
}
