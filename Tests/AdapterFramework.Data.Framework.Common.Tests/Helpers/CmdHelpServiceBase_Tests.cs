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
using System.Runtime.InteropServices;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Common.Helpers;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Helpers;

public class CmdHelpServiceBase_Tests
{
    [Fact]
    public void CmdConfigServiceBase_GetLaunchCommand_Test()
    {
        var expectedCommand = IsWindows() ? ".\\" : "./";
        var command = CmdHelpServiceBase.GetLaunchCommand();

        Assert.Equal(expectedCommand, command);
    }

    [Fact]
    public void CmdConfigServiceBase_GetApplicationLaunchCommand_Test()
    {
        var utilityName = "UnitTests";
        var expectedCommand = $"{CmdHelpServiceBase.GetLaunchCommand()}{utilityName}{(IsWindows() ? ".exe" : string.Empty)}";
        var command = CmdHelpServiceBase.GetApplicationLaunchCommand(utilityName);

        Assert.Equal(expectedCommand, command);
    }

    [Fact]
    public void CmdConfigServiceBase_GetConfigurationCommandBase_Test()
    {
        var expectedCommand = $"{CmdHelpServiceBase.GetApplicationLaunchCommand(EdgeSystemConstants.CmdUtilityName)} {EdgeSystemConstants.ConfigurationKeyword}";
        var command = CmdHelpServiceBase.GetConfigurationCommandBase();

        Assert.Equal(expectedCommand, command);
    }

    [Fact]
    public void CmdConfigServiceBase_GetAdministrationCommandBase_Test()
    {
        var expectedCommand = $"{CmdHelpServiceBase.GetApplicationLaunchCommand(EdgeSystemConstants.CmdUtilityName)} {EdgeSystemConstants.AdministrationKeyword}";
        var command = CmdHelpServiceBase.GetAdministrationCommandBase();

        Assert.Equal(expectedCommand, command);
    }

    [Fact]
    public void CmdConfigServiceBase_GetHelpCommandBase_Test()
    {
        var expectedCommand = $"{CmdHelpServiceBase.GetApplicationLaunchCommand(EdgeSystemConstants.CmdUtilityName)} {EdgeSystemConstants.HelpKeyword}";
        var command = CmdHelpServiceBase.GetHelpCommandBase();

        Assert.Equal(expectedCommand, command);
    }

    private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
}
