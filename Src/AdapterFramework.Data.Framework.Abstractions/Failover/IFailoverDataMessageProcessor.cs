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
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Abstractions.Failover;

public interface IFailoverDataMessageProcessor : IDisposable
{
    /// <summary>
    /// Get the current failover mode set by the client failover configuration.
    /// </summary>
    FailoverMode CurrentFailoverMode { get; }

    /// <summary>
    /// Get the current failover role received from the failover endpoint.
    /// </summary>
    FailoverRole CurrentFailoverRole { get; }

    /// <summary>
    /// Get the last data process time stamp.
    /// </summary>
    DateTime LastDataProcessedTime { get; }

    /// <summary>
    /// Initialize the failover data message processor.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Process the incoming OMF message.
    /// </summary>
    /// <param name="message">The OMF message to process.</param>
    void ProcessOmfMessage(ISerializedOmfMessage message);

    /// <summary>
    /// Update the state of the failover data message processor with role and last processed time changes.
    /// </summary>
    /// <param name="role">The new role to update.</param>
    /// <param name="lastDataProcessedTime">The new last data processed time.</param>
    void UpdateState(FailoverRole role, DateTime lastDataProcessedTime);

    /// <summary>
    /// Update the failover mode.
    /// </summary>
    /// <param name="newFailoverMode">The failover mode to update.</param>
    void UpdateMode(FailoverMode newFailoverMode);
}
