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
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.Abstractions.Discovery;

public interface IDataSourceDiscoveryService<T> where T : IDataSelectionConfiguration
{
    /// <summary>
    /// Returns ID of the discovery operation.
    /// </summary>
    string DiscoveryId { get; }

    /// <summary>
    /// Adds or updates discovered selection item to the collection of items managed by the framework.
    /// </summary>
    /// <param name="selectionItem">Discovered data selection item to add.</param>
    void AddOrUpdateSelectionItem(T selectionItem);

    /// <summary>
    /// Updates progress property in the <see cref="DiscoveryState"/> object to inform user about the discovery progress.
    /// </summary>
    /// <param name="discoveryProgress">Adapter specific number representing progress of the discovery.</param>
    void UpdateProgress(int discoveryProgress);
}
