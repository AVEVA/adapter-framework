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
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests;

public class AdapterCmdHelpService_Tests
{
    private const string UnitTestComponentId = "UnitTest";

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void AdapterCmdHelpService_Constructor_InvalidInput(string componentId)
    {
        var exceptionThrown = false;

        try
        {
            var adapterCmdHelpService = new AdapterCmdHelpService(componentId);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public void AdapterCmdHelpService_GetConfigurationCommandBaseString_Test()
    {
        var commandBaseString = AdapterCmdHelpService.GetConfigurationCommandBaseString();

        Assert.StartsWith(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
            ? ".\\" 
            : "./",
            commandBaseString, StringComparison.InvariantCulture);
    }

    [Fact]
    public void AdapterCmdHelpService_GetHelpHeaderString_Test()
    {
        var facetName = "TestFacet";
        var adapterCmdHelpService = new AdapterCmdHelpService(UnitTestComponentId);

        var helpHeaderText = adapterCmdHelpService.GetHelpHeaderString(facetName);

        Assert.Contains(facetName, helpHeaderText, StringComparison.InvariantCulture);
    }

    [Fact]
    public void AdapterCmdHelpService_GetLoggingHelpOutput_Test()
    {
        var loggingFacetName = "Logging";
        var adapterCmdHelpService = new AdapterCmdHelpService(UnitTestComponentId);

        var loggingHelpOutput = adapterCmdHelpService.GetLoggingHelpOutput();

        Assert.Contains(loggingFacetName, loggingHelpOutput, StringComparison.InvariantCulture);
        Assert.DoesNotContain(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                ? ".\\" 
                : "./",
            loggingHelpOutput, StringComparison.InvariantCulture);
    }
}
