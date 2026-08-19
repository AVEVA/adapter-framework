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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.AdapterCommon.Discovery;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Discovery;

public class DataSourceDiscoveryService_Tests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 15)]
    [InlineData(10, 100)]
    public void DataSourceDiscoveryService_AddConfigurationsUpdateProgressTest(int configurationCount, int progressCount)
    {
        const string DiscoveryId = "Discovery1";

        var configDiscoveryState = new Mock<DiscoveryState>();
        configDiscoveryState.Object.Id = DiscoveryId;

        var dataSelectionConfigurations = new TestDataSelectionWithId[configurationCount];
        for (int i = 0; i < configurationCount; i++)
        {
            dataSelectionConfigurations[i] = new TestDataSelectionWithId($"StreamId{i}");
        }

        var dataSourceDiscoveryService = new DataSourceDiscoveryService<IDataSelectionConfiguration>(configDiscoveryState.Object);

        Parallel.ForEach(dataSelectionConfigurations, (config) =>
        {
            dataSourceDiscoveryService.AddOrUpdateSelectionItem(config);
            dataSourceDiscoveryService.UpdateProgress(progressCount);
        });

        Assert.Equal(DiscoveryId, dataSourceDiscoveryService.DiscoveryId);
        Assert.Equal(configurationCount, dataSourceDiscoveryService.DiscoveredItems.Count);
        var streamIdSet = new HashSet<string>(new[] { "StreamId0", $"StreamId{configurationCount - 1}" });

        Assert.True(streamIdSet.IsSubsetOf(dataSourceDiscoveryService.DiscoveredItems.Keys));
        Assert.Equal(progressCount, configDiscoveryState.Object.Progress);
        Assert.Equal(configurationCount, configDiscoveryState.Object.ItemsFound);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void DataSourceDiscoveryService_UpdateConfigurations(int configurationCount)
    {
        const string DiscoveryId = "Discovery1";

        var configDiscoveryState = new Mock<DiscoveryState>();
        configDiscoveryState.Object.Id = DiscoveryId;

        var dataSelectionConfigurations = new TestDataSelectionWithId[configurationCount];
        for (int i = 0; i < configurationCount; i++)
        {
            dataSelectionConfigurations[i] = new TestDataSelectionWithId($"StreamId{i}", name: null);
        }

        var streamIdSet = new HashSet<string>(new[] { "StreamId0", $"StreamId{configurationCount - 1}" });

        var dataSourceDiscoveryService = new DataSourceDiscoveryService<IDataSelectionConfiguration>(configDiscoveryState.Object);

        Parallel.ForEach(dataSelectionConfigurations, (config) =>
        {
            dataSourceDiscoveryService.AddOrUpdateSelectionItem(config);
        });

        Assert.Equal(DiscoveryId, dataSourceDiscoveryService.DiscoveryId);
        Assert.Equal(configurationCount, dataSourceDiscoveryService.DiscoveredItems.Count);
        Assert.True(streamIdSet.IsSubsetOf(dataSourceDiscoveryService.DiscoveredItems.Keys));
        for (int i = 0; i < configurationCount; i++)
        {
            Assert.Null(dataSelectionConfigurations[i].Name);
        }

        for (int i = 0; i < configurationCount; i++)
        {
            dataSelectionConfigurations[i].Name = $"Name{i}";
            dataSelectionConfigurations[i].StreamId = $"stREamID{i}";
        }

        Parallel.ForEach(dataSelectionConfigurations, (config) =>
        {
            dataSourceDiscoveryService.AddOrUpdateSelectionItem(config);
        });

        Assert.Equal(DiscoveryId, dataSourceDiscoveryService.DiscoveryId);
        Assert.Equal(configurationCount, dataSourceDiscoveryService.DiscoveredItems.Count);
        Assert.True(dataSourceDiscoveryService.DiscoveredItems.ContainsKey(streamIdSet.FirstOrDefault()));
        Assert.True(dataSourceDiscoveryService.DiscoveredItems.ContainsKey(streamIdSet.LastOrDefault()));
        for (int i = 0; i < configurationCount; i++)
        {
            Assert.Equal(dataSelectionConfigurations[i].Name, $"Name{i}");
        }

        Assert.Equal(configurationCount, configDiscoveryState.Object.ItemsFound);
    }
}
