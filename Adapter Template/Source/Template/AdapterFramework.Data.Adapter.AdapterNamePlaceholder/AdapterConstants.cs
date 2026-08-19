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
namespace AdapterFramework.Data.Adapter.AdapterNamePlaceholder;

/// <summary>
/// Contains constant values used throughout the adapter.
/// </summary>
public static class AdapterConstants
{
    /// <summary>
    /// The component type identifier for the adapter.
    /// </summary>
    public const string ComponentType = "AdapterNamePlaceholder";

    /// <summary>
    /// The default stream ID pattern used for generating stream identifiers.
    /// </summary>
    // TODO: Replace the default stream ID pattern with adapter specific content. 
    public static readonly string DefaultStreamIdPattern = "{AdapterNamePlaceholder}";

    /// <summary>
    /// The default keywords used in stream ID generation.
    /// </summary>
    // TODO: Replace the default stream ID keywords with adapter specific content. 
    public static readonly string[] DefaultStreamIdKeywords = ["AdapterNamePlaceholder"];

    // TODO: Define other constants if necessary. 
}
