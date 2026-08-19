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
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Health;

namespace AdapterFramework.Data.Framework.Abstractions.Failover;

public interface IFailoverEndpointManager : IDisposable
{
    /// <summary>
    /// Initialize the failover endpoint manager by providing the role change callback.
    /// </summary>
    /// <param name="failoverRoleChangeAction">The role change callback action to register to the instance.</param>
    /// <param name="failoverScoreFunc">The callback to obtain the failover score.</param>
    /// <param name="deviceStatusAction">The callback to update the device status.</param>
    void Initialize(Action<FailoverRole, FailoverRole> failoverRoleChangeAction, Func<float> failoverScoreFunc, Action<DeviceStatus> deviceStatusAction);

    /// <summary>
    /// Start the failover endpoint manager instance.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel the start.</param>
    /// <returns>The starting task.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the failover manager instance and release any resources used by it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel the stop.</param>
    /// <returns>The stopping task.</returns>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Update the failover configuration for the failover endpoint manager instance.
    /// </summary>
    /// <param name="configurationChangeEvent">The failover configuration change event arguments.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the configuration update.</param>
    /// <returns>The updating task.</returns>
    Task UpdateConfigurationAsync(ConfigurationChangedEventArgs configurationChangeEvent, CancellationToken cancellationToken);
}
