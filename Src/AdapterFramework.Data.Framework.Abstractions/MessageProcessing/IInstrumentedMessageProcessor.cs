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
using AdapterFramework.Data.DataModel;

namespace AdapterFramework.Data.Framework.Abstractions.MessageProcessing;

/// <summary>
/// Represents a type used to process Data messages and provide instrumentation like
/// number of created streams, types and number of events passed through the component
/// </summary>
public interface IInstrumentedMessageProcessor : IMessageProcessor
{
    /// <summary>
    /// Sets streamIdPrefix string to be used to prefix any <see cref="DataStream.Id"/> passing the <see cref="IInstrumentedMessageProcessor"/>.
    /// </summary>
    /// <param name="streamIdPrefix">String to be used as a <see cref="DataStream.Id"/> prefix.</param>
    void SetStreamIdPrefix(string streamIdPrefix);

    /// <summary>
    /// Gets current number of <see cref="DataStream"/> sent through the processor.
    /// </summary>
    /// <returns>Current <see cref="DataStream"/> count.</returns>
    int GetStreamCount();

    /// <summary>
    /// Gets current number of <see cref="DataType"/> sent through the processor.
    /// </summary>
    /// <returns>Current <see cref="DataType"/> count.</returns>
    int GetTypeCount();

    /// <summary>
    /// Gets the current number of unique OMF 2.0 entity identities accepted by the processor and retained.
    /// Fed by the <c>WriteStaticValue&lt;T&gt;(typeId, id, name, ...)</c> overloads; acceptance by the processor
    /// is not a delivery guarantee. Implementations relying on the default interface member report 0.
    /// </summary>
    /// <returns>Current asset count.</returns>
    int GetAssetCount() => 0;

    /// <summary>
    /// Gets the number of OMF 2.0 event writes accepted by the processor since start or the last <see cref="ClearCounters"/>.
    /// Fed by <c>WriteEvent&lt;T&gt;</c> excluding deletes; acceptance by the processor is not a delivery guarantee.
    /// Implementations relying on the default interface member report 0.
    /// </summary>
    /// <returns>Current event write count.</returns>
    long GetEventWriteCount() => 0;

    /// <summary>
    /// Gets and resets total number of data events sent through the processor and resets
    /// the counter back to 0.
    /// </summary>
    /// <returns>Total number events from the last time this method was called.</returns>
    long GetAndResetEventsCounter();

    /// <summary>
    /// Clears every counter in the <see cref="IInstrumentedMessageProcessor"/> service. Number of Types, Streams
    /// and Events are going to be cleared.
    /// </summary>
    void ClearCounters();
}
