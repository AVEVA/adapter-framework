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
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel.Converters;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object representing an OMF event type message.
/// </summary>
public class EventDataType : DataType
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventDataType"/> class.
    /// </summary>
    public EventDataType()
    {
        Type = Tokens.ObjectToken;
    }

    /// <inheritdoc/>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Classification)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public override string Classification => Tokens.Event;
}
