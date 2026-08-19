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
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;

namespace AdapterFramework.Data.Framework.Abstractions.Discovery;

public interface IDataSourceDiscoveryManager
{
    /// <summary>
    /// Returns discovery results object for the given <paramref name="discoveryId"/>.
    /// </summary>
    /// <param name="discoveryId">Id of the discovery operation to return result for.</param>
    /// <param name="discoveryOptions"><see cref="DiscoveryOptions"/> instance to argument the GET operation.</param>
    /// <returns><see cref="MvcResult"/> containing payload and representing status of the operation.</returns>
    MvcResult GetDiscoveryResult(string discoveryId, DiscoveryOptions discoveryOptions);

    /// <summary>
    /// Deletes discovery results object (keeps the status) for the given <paramref name="discoveryId"/> and cancels the discovery if in progress.
    /// </summary>
    /// <param name="discoveryId">Id of the discovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult DeleteDiscoveryResult(string discoveryId);

    /// <summary>
    /// Deletes discovery status as well as the discovery results object for the given <paramref name="discoveryId"/> and cancels the discovery if in progress.
    /// </summary>
    /// <param name="discoveryId">Id of the discovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult DeleteDiscovery(string discoveryId);

    /// <summary>
    /// Deletes entire discovery status collection with discovery results objects. Discovery in progress is going to be cancelled.
    /// </summary>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult DeleteDiscoveries();

    /// <summary>
    /// Starts a new data source discovery operation.
    /// </summary>
    /// <param name="discoveryState"><see cref="DiscoveryState"/> object containing information about the discovery.</param>
    /// <param name="discoveryOptions"><see cref="DiscoveryOptions"/> augmenting the discovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult StartDiscovery(DiscoveryState discoveryState, DiscoveryOptions discoveryOptions);

    /// <summary>
    /// Cancels discovery operation for the given <paramref name="discoveryId"/>.
    /// </summary>
    /// <param name="discoveryId">Id of the discovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult CancelDiscovery(string discoveryId);

    /// <summary>
    /// Merges <paramref name="discoveryId"/> result with the existing data selection configuration and notifies the adapter when finished.
    /// </summary>
    /// <param name="discoveryId">Id of the discovery operation to get results from.</param>
    /// <param name="selected">When True the items are going to be added as selected and vice versa.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult MergeWithDataSelection(string discoveryId, bool selected);

    /// <summary>
    /// Returns set of data selection items that are in the discovery result and were not found in data selection configuration. 
    /// </summary>
    /// <param name="discoveryId">Id of the discovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    /// <remarks>Comparison is done by StreamId property.</remarks>
    MvcResult GetDataSelectionDifference(string discoveryId);

    /// <summary>
    /// Returns set of data selection items that are present in <paramref name="discoveryIdB"/> and not in <paramref name="discoveryIdA"/>.
    /// </summary>
    /// <param name="discoveryIdA">Id of discovery operation result to compare to <paramref name="discoveryIdB"/>.</param>
    /// <param name="discoveryIdB">Id of discovery operation result to compare to <paramref name="discoveryIdA"/>.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    /// <remarks>Comparison is done by StreamId property.</remarks>
    MvcResult GetDiscoveriesDifference(string discoveryIdA, string discoveryIdB);

    /// <summary>
    /// Returns entire <see cref="DiscoveryState"/> configuration.
    /// </summary>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    public MvcResult GetDiscoveryStates();

    /// <summary>
    /// Returns <see cref="DiscoveryState"/> configuration by <paramref name="discoveryId"/>.
    /// </summary>
    /// <param name="discoveryId">Id of discovery operation to return the <see cref="DiscoveryState"/> for.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    public MvcResult GetDiscoveryState(string discoveryId);
}
