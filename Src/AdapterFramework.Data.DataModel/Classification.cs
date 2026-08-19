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
namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An Enum for OMF Type Classification.
/// </summary>
public enum Classification 
{
    /// <summary> OMF Static Type.</summary>
    Static = 0,

    /// <summary> OMF Dynamic Type.</summary>
    Dynamic = 1,

    /// <summary>OMF Streaming Data Type.</summary>
    StreamingData = 2,

    /// <summary>OMF Entity Type.</summary>
    Entity = 3,

    /// <summary>OMF Event Type.</summary>
    Event = 4,
}
