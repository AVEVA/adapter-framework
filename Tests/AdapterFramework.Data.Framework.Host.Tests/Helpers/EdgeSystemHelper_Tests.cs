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
using Microsoft.Extensions.Configuration;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Helpers;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Helpers;

/// <summary>
/// Tests how <see cref="EdgeSystemHelper"/> reads startup settings from the appsettings configuration.
/// </summary>
public class EdgeSystemHelper_Tests
{
    /// <summary>
    /// Verifies that the active awaitable scope limit falls back to the coordinator's default when appsettings doesn't set it.
    /// </summary>
    [Fact]
    public void GetMaxActiveAwaitableScopes_WhenNotSet_ReturnsDefault()
    {
        var configuration = CreateConfiguration(null);

        Assert.Equal(OmfAwaitableCoordinator.DefaultMaxActiveScopes, EdgeSystemHelper.GetMaxActiveAwaitableScopes(configuration));
    }

    /// <summary>
    /// Verifies that a positive limit set in appsettings is used as configured.
    /// </summary>
    [Theory]
    [InlineData("1", 1)]
    [InlineData("5000", 5000)]
    public void GetMaxActiveAwaitableScopes_WhenPositive_ReturnsConfiguredValue(string setting, int expected)
    {
        var configuration = CreateConfiguration(setting);

        Assert.Equal(expected, EdgeSystemHelper.GetMaxActiveAwaitableScopes(configuration));
    }

    /// <summary>
    /// Verifies that a limit below 1 fails startup with <see cref="ArgumentException"/> instead of creating a coordinator that rejects every scope.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void GetMaxActiveAwaitableScopes_WhenLessThanOne_Throws(string setting)
    {
        var configuration = CreateConfiguration(setting);

        Assert.Throws<ArgumentException>(() => EdgeSystemHelper.GetMaxActiveAwaitableScopes(configuration));
    }

    private static IConfiguration CreateConfiguration(string maxActiveScopes) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { [ConfigurationConstants.MaxActiveAwaitableScopesKey] = maxActiveScopes })
            .Build();
}
