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
using System.Linq;
using System.Text.Json;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Failover.Configuration;
using Xunit;

namespace AdapterFramework.Data.Framework.Failover.Tests.Configuration;

public class ClientFailoverConfiguration_Tests
{
    private const string DefaultFailoverGroupId = "FailoverGroupId";
    private const int DefaultFailoverTimeOutInSeconds = 20;

    [Theory]
    [InlineData(DefaultFailoverGroupId, DefaultFailoverTimeOutInSeconds)]
    [InlineData(DefaultFailoverGroupId, 2)]
    [InlineData(null, DefaultFailoverTimeOutInSeconds)]
    [InlineData("", DefaultFailoverTimeOutInSeconds)]
    [InlineData("NewFailoverGroupId", DefaultFailoverTimeOutInSeconds)]
    [InlineData("NewFailoverGroupId", 2)]
    public void ClientFailoverConfiguration_Equals_Test(string groupId, int timeOutInSeconds)
    {
        var configuration1 = CreateDefaultConfiguration();

        var configuration2 = CreateDefaultConfiguration();
        configuration2.FailoverGroupId = groupId;
        configuration2.FailoverTimeout = TimeSpan.FromSeconds(timeOutInSeconds);

        if (groupId == DefaultFailoverGroupId && timeOutInSeconds == DefaultFailoverTimeOutInSeconds)
        {
            Assert.Equal(configuration1, configuration2);
        }
        else
        {
            Assert.NotEqual(configuration1, configuration2);
        }
    }

    [Theory]
    [InlineData(FailoverMode.Cold, true)]
    [InlineData(FailoverMode.Hot, true)]
    [InlineData(FailoverMode.Warm, true)]
    [InlineData(FailoverMode.NotConfigured, false)]
    public void ClientFailoverConfiguration_Mode_Test(FailoverMode failoverMode, bool isValid)
    {
        var failoverConfiguration = CreateDefaultConfiguration();
        failoverConfiguration.Mode = failoverMode;

        var errors = failoverConfiguration.Validate();

        Assert.Equal(isValid, !errors.Any());
    }

    [Theory]
    [InlineData("groupId", true)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData(null, false)]
    public void ClientFailoverConfiguration_GroupId_Test(string groupId, bool isValid)
    {
        var failoverConfiguration = CreateDefaultConfiguration();
        failoverConfiguration.FailoverGroupId = groupId;

        var errors = EdgeConfigurationBase.ValidateConfiguration(failoverConfiguration);

        Assert.Equal(isValid, !errors.Any());
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(30, true)]
    [InlineData(10, false)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    public void ClientFailoverConfiguration_FailoverTimeout_Test(int failoverTimeout, bool isValid)
    {
        var failoverConfiguration = CreateDefaultConfiguration();
        failoverConfiguration.FailoverTimeout = TimeSpan.FromSeconds(failoverTimeout);

        var errors = failoverConfiguration.Validate();

        Assert.Equal(isValid, !errors.Any());
    }

    [Fact]
    public void ClientFailoverConfiguration_DefaultTimeoutAndMode_Test()
    {
        var failoverConfiguration = new ClientFailoverConfiguration()
        {
            Endpoint = "https://helloWord",
        };

        Assert.Equal(FailoverMode.Hot, failoverConfiguration.Mode);
        Assert.Equal(TimeSpan.FromSeconds(60), failoverConfiguration.FailoverTimeout);
        Assert.False(failoverConfiguration.EnableAcceptanceTrimWatermark);
    }

    /// <summary>
    /// Verifies that existing JSON without the opt-in retains legacy hand-off timestamp trimming.
    /// </summary>
    [Fact]
    public void Deserialize_WhenAcceptanceTrimWatermarkOmitted_DefaultsToFalse()
    {
        var configuration = JsonSerializer.Deserialize<ClientFailoverConfiguration>("{}");

        Assert.False(configuration.EnableAcceptanceTrimWatermark);
    }

    /// <summary>
    /// Verifies that an explicit watermark setting is writable and survives JSON serialization and deserialization.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnableAcceptanceTrimWatermark_WhenExplicitlySet_RoundTrips(bool enabled)
    {
        var configuration = CreateDefaultConfiguration();
        configuration.EnableAcceptanceTrimWatermark = enabled;

        var json = JsonSerializer.Serialize(configuration);
        var deserialized = JsonSerializer.Deserialize<ClientFailoverConfiguration>(json);

        Assert.Equal(enabled, configuration.EnableAcceptanceTrimWatermark);
        Assert.Equal(enabled, deserialized.EnableAcceptanceTrimWatermark);
        Assert.Equal(configuration, deserialized);
    }

    /// <summary>
    /// Verifies that equal watermark settings produce equal configurations and hashes, while a setting-only change is detected.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equals_WhenAcceptanceTrimWatermarkChanges_DetectsChange(bool enabled)
    {
        var configuration = CreateDefaultConfiguration();
        configuration.EnableAcceptanceTrimWatermark = enabled;
        var other = CreateDefaultConfiguration();
        other.EnableAcceptanceTrimWatermark = enabled;

        Assert.True(configuration.Equals(other));
        Assert.True(configuration.Equals((object)other));
        Assert.Equal(configuration.GetHashCode(), other.GetHashCode());

        other.EnableAcceptanceTrimWatermark = !enabled;

        Assert.False(configuration.Equals(other));
        Assert.False(configuration.Equals((object)other));
        Assert.NotEqual(configuration.GetHashCode(), other.GetHashCode());
    }

    /// <summary>
    /// Verifies that CLI help explains the legacy default and the matching opt-in required for hot failover scopes.
    /// </summary>
    [Fact]
    public void GetClientFailoverHelpOutput_WhenRequested_DescribesAcceptanceTrimWatermark()
    {
        var output = new FailoverCmdHelpService().GetClientFailoverHelpOutput();

        Assert.Contains(nameof(ClientFailoverConfiguration.EnableAcceptanceTrimWatermark), output);
        Assert.Contains("Defaults to false", output);
        Assert.Contains("Both nodes must use the same value", output);
        Assert.Contains("hot failover awaitable scopes require true on both nodes", output);
    }

    private static ClientFailoverConfiguration CreateDefaultConfiguration()
    {
        return new ClientFailoverConfiguration()
        {
            Endpoint = "https://helloWorld",
            Mode = FailoverMode.Cold,
            FailoverGroupId = DefaultFailoverGroupId,
            FailoverTimeout = TimeSpan.FromSeconds(DefaultFailoverTimeOutInSeconds),
        };
    }
}
