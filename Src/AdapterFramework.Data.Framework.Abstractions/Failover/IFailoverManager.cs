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
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Health;

namespace AdapterFramework.Data.Framework.Abstractions.Failover;

public interface IFailoverManager : IDisposable
{
    /// <summary>
    /// Initialize the failover manager instance with supported failover modes.
    /// </summary>
    /// <param name="supportedFailoverModes">The supported failover modes.</param>
    void Initialize(FailoverMode supportedFailoverModes);

    /// <summary>
    /// Register a failover mode change callback for the component with provided component ID.
    /// </summary>
    /// <param name="componentId">The ID of the component to register the callback.</param>
    /// <param name="failoverModeChangeCallback">The callback returning the old and new failover modes.</param>
    void RegisterFailoverModeChangeCallback(string componentId, Func<FailoverMode, FailoverMode, Task> failoverModeChangeCallback);

    /// <summary>
    /// Unregister the failover mode change callback for the component with provided component ID.
    /// </summary>
    /// <param name="componentId">The ID of the component to unregister the callback.</param>
    void UnregisterFailoverModeChangeCallback(string componentId);

    /// <summary>
    /// Register a failover role change callback for the component with provided component ID.
    /// </summary>
    /// <param name="componentId">The ID of the component to register the callback.</param>
    /// <param name="failoverRoleChangeCallback">The callback returns the old and new failover roles.</param>
    void RegisterFailoverRoleChangeCallback(string componentId, Func<FailoverRole, FailoverRole, Task> failoverRoleChangeCallback);

    /// <summary>
    /// Unregister the failover role change callback for the component with provided component ID.
    /// </summary>
    /// <param name="componentId">The ID of the component to unregister the callback.</param>
    void UnregisterFailoverRoleChangeCallback(string componentId);

    /// <summary>
    /// Start the failover manager instance
    /// </summary>
    /// <param name="cancellationToken">The cancellation token instance to cancel the start.</param>
    /// <returns>The starting task.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stop the failover manager instance and release any resources used by it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token instance to cancel the stop.</param>
    /// <returns>The stopping task.</returns>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Update the failover configuration for the failover manager instance.
    /// </summary>
    /// <param name="configurationChangeEvent">The configuration change event containing the failover configuration to update.</param>
    void UpdateFailoverConfiguration(ConfigurationChangedEventArgs configurationChangeEvent);

    /// <summary>
    /// Custom validation logic to validate the array of failover configurations.
    /// </summary>
    /// <param name="configurationChangeEvent">The configuration change event containing the failover configuration to validate.</param>
    /// <returns>The collection of error messages.</returns>
    ICollection<string> ValidateFailoverConfiguration(ConfigurationChangedEventArgs configurationChangeEvent);

    /// <summary>
    /// Get the current failover state.
    /// </summary>
    /// <returns>The current failover state.</returns>
    FailoverState GetCurrentFailoverState();

    /// <summary>
    /// Adds a component health service to failover manager.
    /// </summary>
    /// <param name="componentId">Component id of the health service.</param>
    /// <param name="healthService">The <see cref="IFailoverService"/>.</param>
    void AddComponentHealthService(string componentId, IFailoverService healthService);

    /// <summary>
    /// Removes a component's health service from the failover manager.
    /// </summary>
    /// <param name="componentId">Component id of the health service.</param>
    void RemoveComponentHealthService(string componentId);

    /// <summary>
    /// Resends the types, streams, and data for failover health and diagnostics.
    /// </summary>
    void ResendHealthAndDiagnostics();
}
