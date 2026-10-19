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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.Failover;

namespace AdapterFramework.Data.Framework.Host.Tests.Helpers;

internal sealed class TestEdgeAdapter : IEdgeAdapter
{
    public string ComponentId => nameof(TestEdgeAdapter);

    public string ComponentType => nameof(TestEdgeAdapter);

    public FailoverMode SupportedFailoverModes => FailoverMode.NotConfigured;

    public Channel<IEdgeEvent> EdgeEventChannel { get; } = Channel.CreateUnbounded<IEdgeEvent>();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ProcessGeneralConfigurationUpdateCallbackAsync(IAdapterGeneralConfiguration oldConfig, IAdapterGeneralConfiguration newConfig) => Task.CompletedTask;

    public void Register(CancellationToken cancellationToken)
    {
    }

    public void ResendDynamicMetadata()
    {
    }

    public void ResendHealthMetadata()
    {
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Unregister(CancellationToken cancellationToken)
    {
    }
}

internal sealed class TestEdgeService : IEdgeService
{
    public string ComponentId => nameof(TestEdgeService);

    public string ComponentType => nameof(TestEdgeService);

    public Channel<IEdgeEvent> EventChannel { get; } = Channel.CreateUnbounded<IEdgeEvent>();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public void ResendHealthMetadata()
    {
    }
}

internal sealed class TestFailoverServiceProvider : IFailoverServiceProvider
{
    public string ComponentId => nameof(TestFailoverServiceProvider);

    public string ComponentType => nameof(TestFailoverServiceProvider);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void ResendHealthMetadata()
    {
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// This implementation is used to test that the GetComponentImplementations method does not return non-legacy candidates when the prefix does not match.
internal sealed class TestSinkProvider : ISinkProvider
{
    public string ComponentId => nameof(TestSinkProvider);

    public string ComponentType => nameof(TestSinkProvider);

    public Task InitializeAsync() => Task.CompletedTask;

    public void ResendHealthMetadata()
    {
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;
}
