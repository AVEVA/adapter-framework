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
using AdapterFramework.Data.Framework.Abstractions.Components;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Represents a service that provides Edge Component ID provisioning
/// </summary>
public interface IComponentIdService
{
    /// <summary>
    /// Returns <see cref="IEdgeComponent"/> Component ID based on provided Component Type.
    /// </summary>
    /// <param name="edgeComponentType">Type of an <see cref="IEdgeComponent"/> component to get ID for.</param>
    /// <returns>Unique ID configured for the requested Edge System component type.</returns>
    string GetEdgeComponentId(string edgeComponentType);

    /// <summary>
    /// Adds <see cref="IEdgeComponent"/> a new <paramref name="componentId"/> to the collection of available IDs for
    /// the given <paramref name="componentType"/>.
    /// </summary>
    /// <param name="componentType">Type of <see cref="IEdgeComponent"/> component to add ID for.</param>
    /// <param name="componentId">Id to add to the collection.</param>
    void AddEdgeComponentId(string componentType, string componentId);
}
