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
using System.Collections.Generic;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <summary>
/// Provides functionality to interact with OMF endpoints.
/// </summary>
public interface IOmfEndpointManager
{
    /// <summary>
    /// Initialize the IOmfEndpointManager to load the endpoint configurations and create the <see cref="IOmfWriter"/>s.
    /// </summary>
    /// <param name="componentId">ID of a component the endpoint manager should be initialized for.</param>
    /// <param name="facet">Facet from where the endpoint manager should load its configuration.</param>
    void Initialize(string componentId, string facet);

    /// <summary>
    /// Send a serialized OMF message synchronously to all <see cref="IOmfWriter"/>s managed by the IOmfEndpointManager.
    /// </summary>
    /// <param name="serializedMessage">The serialized OMF message instance to be sent.</param>
    void SendMessage(ISerializedOmfMessage serializedMessage);

    /// <summary>
    /// Adds or removes OMF endpoints based on <paramref name="configurationChangedEvent"/> content.
    /// </summary>
    /// <param name="configurationChangedEvent">Configuration change event retrieved from the controller.</param>
    /// <param name="writerType">Type of the <see cref="OmfWriterType"/> instance.</param>
    /// <returns>True when at least one OMF endpoint was added; False otherwise.</returns>
    bool AddRemoveEndpoints(ConfigurationChangedEventArgs configurationChangedEvent, OmfWriterType writerType);

    /// <summary>
    /// Gets the number of data that was sent successfully from all endpoints and then reset their counters.
    /// </summary>
    /// <returns>The number of data that was sent successfully.</returns>
    IReadOnlyDictionary<string, long> GetAndResetEgressedValuesCounters();

    /// <summary>
    /// Resets data buffers by deleting and recreating all internal OmfWriters.
    /// </summary>
    /// <returns>The asynchronously running task for sending the message.</returns>
    Task ResetDataBuffersAsync();

    /// <summary>
    /// Updates the buffer size for the endpoints managed by the IOmfEndpointManager.
    /// </summary>
    /// <param name="newMaxBufferSizeMB">The new buffer size in MB.</param>
    void UpdateBufferSize(int newMaxBufferSizeMB);
}
