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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Health;

namespace AdapterFramework.Data.Framework.Failover.Tests;

public class TestFailoverEndpointManager : IFailoverEndpointManager
{
    public Func<float> FailoverStatusFunc { get; private set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void Initialize(Action<FailoverRole, FailoverRole> failoverRoleChangeAction, Func<float> failoverStatusFunc, Action<DeviceStatus> deviceStatusAction)
    {
        FailoverStatusFunc = failoverStatusFunc;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task UpdateConfigurationAsync(ConfigurationChangedEventArgs configurationChangeEvent, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
