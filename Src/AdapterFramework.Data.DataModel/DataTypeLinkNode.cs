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

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object representing a link node for an OMF Type Message.
/// The link node is for a particular non-container value.
/// </summary>
public class DataTypeLinkNode : LinkNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataTypeLinkNode"/> class.
    /// The link node is for a particular non-container value.
    /// </summary>
    /// <param name="typeId">typeid of an OMF Type object.</param>
    /// <param name="index">Index of an OMF Data object.</param>
    /// <param name="property">Optional name of a property defined in the Type definition to be used by the link relationship.</param>
    /// <param name="type">Optional type of the relationship.</param>
    public DataTypeLinkNode(string typeId, string index, string property = null, string type = null)
        : base(index)
    {
        Id = typeId;
        Property = property;
        Type = type;
    }

    /// <inheritdoc/>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.TypeId)]
    public override string Id { get; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Index)]
    public override string Index => base.Index;
}
