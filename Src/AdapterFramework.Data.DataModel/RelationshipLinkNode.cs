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

public class RelationshipLinkNode : LinkNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RelationshipLinkNode"/> class.
    /// </summary>
    /// <param name="id">Resource identifier.</param>
    /// <param name="typeId">Optional type identifier.</param>
    /// <param name="property">Optional property name.</param>
    /// <param name="type">Optional type of the relationship.</param>
    /// <param name="label">Optional label of the relationship.</param>
    /// <param name="collection">The target collection - types, entities or events.</param>
    public RelationshipLinkNode(string id, string typeId = null, string property = null, string type = null, string label = null, string collection = null)
        : base(null)
    {
        Id = typeId;
        Property = property;
        Type = type;
        Label = label;
        Collection = collection;
        Index = id;
    }

    /// <inheritdoc/>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.TypeId)]
    public override string Id { get; }

    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Id)]
    public override string Index { get; }
}
