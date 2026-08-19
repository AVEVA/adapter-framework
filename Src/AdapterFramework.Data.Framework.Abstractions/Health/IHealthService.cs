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
using AdapterFramework.Data.Framework.Abstractions.Components;

namespace AdapterFramework.Data.Framework.Abstractions.Health;

/// <inheritdoc />
/// <summary>
/// Represents a service used to provide <see cref="IEdgeAdapter"/> component specific Health data.
/// </summary>
public interface IHealthService : IDisposable
{
    /// <summary>
    /// Sends a status massage with the given <paramref name="deviceStatus"/>.
    /// </summary>
    /// <param name="deviceStatus">Optional device status for adapters where one component has ability to connect to multiple devices.</param>
    /// <param name="failoverScore">Optional failover score.</param>
    void SendDeviceStatus(DeviceStatus deviceStatus, float? failoverScore = null);
}
