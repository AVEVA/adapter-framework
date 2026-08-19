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

namespace AdapterFramework.Data.Framework.Abstractions.Events;

/// <summary>
/// Represents an event containing information about buffer file creation/deletion.
/// </summary>
public class BufferFileCreatedDeletedInfo : IEdgeEvent
{
    /// <summary>
    /// Gets the type of edge event. For <see cref="BufferFileCreatedDeletedInfo"/>, this is always <see cref="EdgeEventType.Buffering"/>.
    /// </summary>
    public EdgeEventType EventType => EdgeEventType.Buffering;

    /// <summary>
    /// Gets the UTC timestamp when the event was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the identifier of the endpoint associated with the buffer file creation event.
    /// </summary>
    public string EndpointId { get; }

    /// <summary>
    /// Gets the number of buffer files created.
    /// </summary>
    public int NumberOfFiles { get; }

    /// <summary>
    /// Gets the percentage indicating how full the buffer is.
    /// </summary>
    public double PercentFull { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferFileCreatedDeletedInfo"/> class.
    /// </summary>
    /// <param name="endpointId">The identifier of the endpoint associated with the buffer file creation event.</param>
    /// <param name="numberOfFiles">The number of buffer files created.</param>
    /// <param name="percentFull">The percentage indicating how full the buffer is.</param>
    public BufferFileCreatedDeletedInfo(string endpointId, int numberOfFiles, double percentFull)
    {
        EndpointId = endpointId;
        NumberOfFiles = numberOfFiles;
        PercentFull = percentFull;
    }

    /// <summary>
    /// Returns a string representation of the buffer file creation event.
    /// </summary>
    /// <returns>A string containing the endpoint ID, number of files, and percent full.</returns>
    public override string ToString()
    {
        return $@"
{nameof(EndpointId)}: {EndpointId}
{nameof(NumberOfFiles)}: {NumberOfFiles}
{nameof(PercentFull)}: {PercentFull}%";
    }
}
