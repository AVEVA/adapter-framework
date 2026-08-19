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

namespace AdapterFramework.Data.Framework.Abstractions.Metadata;

/// <summary>
/// Defines metadata verbosity level.
/// </summary>
public enum MetadataInfo
{
    /// <summary>
    /// No metadata.
    /// </summary>
    None,

    /// <summary>
    /// Only the default - framework supplied (DataSource and AdapterType) metadata.
    /// No <see cref="IEdgeComponent"/> specific metadata will be added.
    /// </summary>
    Low,

    /// <summary>
    /// Defines set of <see cref="IEdgeComponent"/> specific key-value pairs to metadata collection that
    /// are approved by Architecture in terms of information disclosure.
    /// </summary>
    Medium,

    /// <summary>
    /// All available metadata will be added.
    /// </summary>
    High,
}
