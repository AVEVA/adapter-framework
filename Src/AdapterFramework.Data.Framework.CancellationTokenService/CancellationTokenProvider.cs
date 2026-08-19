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
using System.Threading;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.CancellationTokenService;

public class CancellationTokenProvider : ICancellationTokenService
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _componentIdToCts =
        new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public CancellationToken GetCancellationToken(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        if (!_componentIdToCts.TryGetValue(componentId, out var cancellationToken))
        {
            cancellationToken = new CancellationTokenSource();
            if (!_componentIdToCts.TryAdd(componentId, cancellationToken))
            {
                cancellationToken.Dispose();
                _componentIdToCts.TryGetValue(componentId, out cancellationToken);
            }
        }

        return cancellationToken.Token;
    }

    public void RequestCancellation(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        if (_componentIdToCts.TryGetValue(componentId, out var cts))
        {
            cts.Cancel();
        }
        else
        {
            throw new InvalidOperationException($"Cancellation token source for {componentId} has not been created. Call GetCancellationToken before cancelling.");
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (var cts in _componentIdToCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
        }

        _disposed = true;
    }
}
