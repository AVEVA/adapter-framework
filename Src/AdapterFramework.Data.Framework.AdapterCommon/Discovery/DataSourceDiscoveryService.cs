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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Discovery;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.Discovery;

public class DataSourceDiscoveryService<TSelection> : IDataSourceDiscoveryService<TSelection> where TSelection : IDataSelectionConfiguration
{
    private readonly ConcurrentDictionary<string, TSelection> _discoveredItems = new ConcurrentDictionary<string, TSelection>(StringComparer.OrdinalIgnoreCase);
    private readonly DiscoveryState _discoveryState;
    private readonly object _lock = new object();
    private int _discoveredItemsCount;

    /// <summary>
    /// Implements <see cref="IDataSourceDiscoveryService{T}"/>
    /// Creates a new instance of the DataDiscovery Service
    /// and provides methods to add or update the DataSelection (AddOrUpdateDataSelection)
    /// and update the progress (UpdateProgress)
    /// </summary>
    /// <param name="discoveryState"> <see cref="DiscoveryState"/> </param>
    public DataSourceDiscoveryService(DiscoveryState discoveryState)
    {
        ThrowHelper.ThrowIfArgumentNull(discoveryState, nameof(discoveryState));

        _discoveryState = discoveryState;
    }

    public string DiscoveryId => _discoveryState.Id;

    public IReadOnlyDictionary<string, TSelection> DiscoveredItems => _discoveredItems;

    public void AddOrUpdateSelectionItem(TSelection selectionItem)
    {
        ThrowHelper.ThrowIfArgumentNull(selectionItem, nameof(selectionItem));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(selectionItem.StreamId, nameof(selectionItem.StreamId));

        _discoveredItems.AddOrUpdate(selectionItem.StreamId, (key) =>
        {
            Interlocked.Increment(ref _discoveredItemsCount);
            return selectionItem;
        }, (key, oldValue) => selectionItem);

        lock (_lock)
        {
            _discoveryState.ItemsFound = _discoveredItemsCount;
        }
    }

    public void UpdateProgress(int discoveryProgress)
    {
        _discoveryState.Progress = discoveryProgress;
    }
}
