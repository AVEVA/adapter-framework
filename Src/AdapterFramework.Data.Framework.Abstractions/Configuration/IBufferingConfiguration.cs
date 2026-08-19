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

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Defines the global egress buffering configuration
/// </summary>
public interface IBufferingConfiguration
{
    /// <summary>
    /// Folder path where the buffer files are to be stored.
    /// </summary>
    string BufferLocation { get; }
    
    /// <summary>
    /// Size of the buffer file(s) on disk or in-memory queue. Must be a positive number.
    /// </summary>
    int MaxBufferSizeMB { get; }

    /// <summary>
    /// Flag to enable or disable disk buffering.
    /// </summary>
    bool EnablePersistentBuffering { get; }

    /// <summary>
    /// Maximum time before batched messages get flushed.
    /// </summary>
    public TimeSpan MaxDataBulkTime { get; }
}
