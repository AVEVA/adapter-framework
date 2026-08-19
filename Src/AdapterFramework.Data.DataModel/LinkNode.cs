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
/// An object representing an OMF Link Node, either the source or the target of an OMF Link Type.
/// </summary>
[JsonConverter(typeof(PolymorphicWriteOnlyJsonConverter<LinkNode>))]
public abstract class LinkNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkNode"/> class.
    /// </summary>
    /// <param name="index"><see cref="Index"></see>.</param>
    protected LinkNode(string index)
    {
        Index = index;
    }

    /// <summary>Gets an ID of the OMF Link Node.</summary>
    /// <value>Either an OMF containerId or an OMF typeid.</value>
    [JsonIgnore]
    public abstract string Id { get; }

    /// <summary>Gets the index of an OMF Data message object.
    /// If an OMF typeid is specified, index is mandatory.
    /// If an OMF containerId is specified it is optional.</summary>
    /// <value>(optional) Index of an OMF Data message object.</value>
    [JsonIgnore]
    public virtual string Index { get; }

    /// <summary>
    /// Gets or sets the name of the property on the OMF Link.
    /// </summary>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.LinkProperty)]
    public string Property { get; init; }

    /// <summary>
    /// Gets or sets the type of the OMF Link.
    /// </summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.LinkType)]
    public string Type { get; init; }

    [JsonPropertyOrder(4)]
    [JsonPropertyName(Tokens.LinkLabel)]
    public string Label { get; init; }

    /// <summary>
    /// Gets or sets the target collection of the OMF Link.
    /// </summary>
    [JsonPropertyOrder(5)]
    [JsonPropertyName(Tokens.LinkCollection)]
    public string Collection { get; set; }
}
