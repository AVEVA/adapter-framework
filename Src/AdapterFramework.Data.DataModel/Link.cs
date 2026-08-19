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

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object representing a OMF Link Type
/// https://docs.aveva.com/bundle/omf/page/1283996.html.
/// </summary>
public class Link
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Link"/> class.
    /// </summary>
    /// <param name="source">An object representing the source of the link.</param>
    /// <param name="target">An object representing the target of the link.</param>
    /// <param name="metadata">An object representing optional metadata associated with the link.</param>
    public Link(LinkNode source, LinkNode target, Dictionary<string, object> metadata = null)
    {
        Source = source;
        Target = target;
        Metadata = metadata;
    }

    /// <summary>Gets an object representing the source of the link.</summary>
    /// <value>An OmfLinkNode object for the source of a link.</value>
    [JsonPropertyOrder(0)]
    [JsonPropertyName(Tokens.Source)]
    public LinkNode Source { get; }

    /// <summary>Gets represents an object representing the target of the link.</summary>
    /// <value>An OmfLinkNode object for the target of a link.</value>
    [JsonPropertyOrder(1)]
    [JsonPropertyName(Tokens.Target)]
    public LinkNode Target { get; }

    /// <summary>Gets or sets optional key-value pairs associated with the link.</summary>
    [JsonPropertyOrder(3)]
    [JsonPropertyName(Tokens.Metadata)]
    public Dictionary<string, object> Metadata { get; }
}
