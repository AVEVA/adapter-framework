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
using System.Collections.Generic;
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel.Converters;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object representing an OMF Data Message schema
/// https://docs.aveva.com/bundle/omf/page/1283994.html.
/// </summary>
[JsonConverter(typeof(PolymorphicWriteOnlyJsonConverter<StreamData>))]
public abstract class StreamData
{
    /// <summary>Gets or sets the ID of the type or the container.</summary>
    /// <value>
    /// Gets Or Sets an ID of the type or the container.
    /// </value>
    [JsonIgnore]
    public abstract string Id { get; set; }

    /// <summary>Gets or sets optional version of the Type, if one is specified.
    /// The version must be of format x.x.x.x, where x must be an integer greater than or equal to 0.
    /// If omitted, version 1.0.0.0 is assumed. </summary>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.TypeVersion)]
    public string TypeVersion { get; set; }

    /// <summary>Gets or sets an array of objects conforming to the type.</summary>
    /// <value>
    /// Gets Or Sets an array of objects conforming to the type.
    /// </value>
    [JsonPropertyOrder(10)]
    [JsonPropertyName(Tokens.Values)]
    [JsonConverter(typeof(RequiredPropertyConverter<IEnumerable<object>>))]
    public IEnumerable<object> Values { get; set; }
}
