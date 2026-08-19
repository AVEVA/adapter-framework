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
using System.Runtime.CompilerServices;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Host.Interfaces;

[assembly: InternalsVisibleTo("AdapterFramework.Data.System.Host.Tests")]
namespace AdapterFramework.Data.Framework.Host.ComponentServices;

internal class EdgeComponentsRepository : IEdgeComponentsRepository
{
    private readonly ConcurrentDictionary<string, IEdgeAdapter> _adapters = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly ConcurrentDictionary<string, IEdgeService> _edgeServices = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly object _sinkProviderLock = new();
    private ISinkProvider _sinkProvider;

    public IEnumerable<IEdgeAdapter> GetAdapters()
    {
        return _adapters.Values;
    }

    public bool TryAddAdapter(IEdgeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(adapter.ComponentId, nameof(adapter.ComponentId));

        return _adapters.TryAdd(adapter.ComponentId, adapter);
    }

    public bool TryGetAdapter(string adapterId, out IEdgeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapterId);

        return _adapters.TryGetValue(adapterId, out adapter);
    }

    public bool TryRemoveAdapter(string adapterId, out IEdgeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapterId);

        return _adapters.TryRemove(adapterId, out adapter);
    }

    public IEnumerable<IEdgeService> GetEdgeServices()
    {
        return _edgeServices.Values;
    }

    public bool TryAddEdgeService(IEdgeService edgeService)
    {
        ArgumentNullException.ThrowIfNull(edgeService);
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(edgeService.ComponentId, nameof(edgeService.ComponentId));

        return _edgeServices.TryAdd(edgeService.ComponentId, edgeService);
    }

    public bool TryGetEdgeService(string edgeServiceId, out IEdgeService edgeService)
    {
        ArgumentNullException.ThrowIfNull(edgeServiceId);

        return _edgeServices.TryGetValue(edgeServiceId, out edgeService);
    }

    public bool TryRemoveEdgeService(string edgeServiceId, out IEdgeService edgeService)
    {
        ArgumentNullException.ThrowIfNull(edgeServiceId);

        return _edgeServices.TryRemove(edgeServiceId, out edgeService);
    }

    public ISinkProvider GetSinkProvider()
    {
        lock (_sinkProviderLock)
        {
            return _sinkProvider;
        }
    }

    public void SetSinkProvider(ISinkProvider sinkProvider)
    {
        lock (_sinkProviderLock)
        {
            _sinkProvider = sinkProvider;
        }
    }
}
