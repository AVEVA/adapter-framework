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
namespace AdapterFramework.Data.Framework.Abstractions.Messages;

/// <summary>
/// Number of OMF 2.0 resource instances, by resource kind, contained in (or successfully delivered from) OMF instance messages.
/// </summary>
/// <remarks>
/// Only streaming values, assets and events are counted. Relationships, types, stream definitions and other schema objects are not.
/// </remarks>
/// <param name="StreamingValues">Number of individual streaming values.</param>
/// <param name="Assets">Number of asset instances.</param>
/// <param name="Events">Number of event instances.</param>
public readonly record struct OmfResourceCounts(long StreamingValues, long Assets, long Events)
{
    /// <summary>
    /// Gets a value indicating whether no resource instances are counted.
    /// </summary>
    public bool IsEmpty => StreamingValues == 0 && Assets == 0 && Events == 0;
}
