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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <inheritdoc />
/// <summary>
/// Represents a type used to send serialized OMF messages.
/// </summary>
public interface IOmfWriter : IDisposable
{
    /// <summary>
    /// Gets the Id.
    /// </summary>
    string Id
    {
        get;
    }

    /// <summary>
    /// Gets the number of values successfully sent since the last call to this method. 
    /// </summary>
    /// <returns>The number of values successfully sent.</returns>
    long GetAndResetEgressedValuesCounter();

    /// <summary>
    /// Updates configuration for an OmfWriter. Must be equivalent to previous configuration (.Equals)
    /// </summary>
    /// <param name="configuration">The configuration to be updated to.</param>
    void UpdateConfiguration(IEndpointConfiguration configuration);

    /// <summary>
    /// Send a serialized OMF message synchronously.
    /// </summary>
    /// <param name="serializedMessage">The serialized OMF message instance to be sent.</param>
    void SendMessage(ISerializedOmfMessage serializedMessage);

    /// <summary>
    /// Deletes created buffers.
    /// </summary>
    void DeleteBuffers();
}
