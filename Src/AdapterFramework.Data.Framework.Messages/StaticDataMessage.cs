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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;

namespace AdapterFramework.Data.Framework.Messages;

public class StaticDataMessage : DataMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StaticDataMessage"/> class.
    /// </summary>
    /// <param name="typeId">The ID of a type.</param>
    /// <param name="id">The ID of the static data message.</param>
    /// <param name="name">The Name of the static data message.</param>
    /// <param name="description">The description of the static data message.</param>
    /// <param name="dataSource">The data source of the static data message.</param>
    /// <param name="tags">The collection of tags for the static data message.</param>
    /// <param name="properties">The extended properties of the static data message.</param>
    /// <param name="propertyOverrides">Optional dictionary that specifies overrides for selected metadata fields on type properties.</param>
    /// <param name="metadata">Optional dictionary of metadata associated with a static data instance.</param>
    /// <param name="instance">The values of the data message.</param>
    /// <param name="messageAction"> The <see cref="MessageAction"/> that will be sent with the message.</param>
    public StaticDataMessage(
        string typeId,
        string id,
        string name,
        string description,
        string dataSource,
        List<string> tags,
        IReadOnlyDictionary<string, PropertyDefinition> properties,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        IReadOnlyDictionary<string, object> metadata,
        object instance,
        MessageAction messageAction)
        : base(typeId, Classification.Static, instance, messageAction)
    {
        InstanceId = id;
        Name = name;
        Description = description;
        DataSource = dataSource;
        Tags = tags;   
        ExtendedPropertiesDefinition = properties;
        PropertyOverrides = propertyOverrides;
        Metadata = metadata;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StaticDataMessage"/> class.
    /// </summary>
    /// <param name="id">The ID of the static data message.</param>
    /// <param name="properties">The extended properties of the static data message.</param>
    /// <param name="propertyOverrides">Optional dictionary that specifies overrides for selected metadata fields on type properties.</param>
    /// <param name="metadata">Optional dictionary of metadata associated with a static data instance.</param>
    /// <param name="instance">The values of the data message.</param>
    /// <param name="messageAction"> The <see cref="MessageAction"/> that will be sent with the message.</param>
    public StaticDataMessage(
        string id,
        IReadOnlyDictionary<string, PropertyDefinition> properties,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        IReadOnlyDictionary<string, object> metadata,
        object instance,
        MessageAction messageAction)
        : base(id, Classification.Static, instance, messageAction)
    {
        ExtendedPropertiesDefinition = properties;
        PropertyOverrides = propertyOverrides;
        Metadata = metadata;
    }

    [JsonPropertyName(Tokens.Id)]
    public string InstanceId { get; set; }

    [JsonPropertyName(Tokens.Name)]
    public string Name { get; set; }

    [JsonPropertyName(Tokens.Description)]
    public string Description { get; set; }

    [JsonPropertyName(Tokens.DataSource)]
    public string DataSource { get; set; }

    [JsonPropertyName(Tokens.Tags)]
    public IEnumerable<string> Tags { get; set; }

    [JsonPropertyName(Tokens.ExtendedPropertiesDefinition)]
    public IReadOnlyDictionary<string, PropertyDefinition> ExtendedPropertiesDefinition { get; }

    [JsonPropertyName(Tokens.PropertyOverrides)]
    public IReadOnlyDictionary<string, PropertyDefinitionOverride> PropertyOverrides { get; }

    [JsonPropertyName(Tokens.Metadata)]
    public IReadOnlyDictionary<string, object> Metadata { get; set; }

    [JsonIgnore]
#pragma warning disable CA2227 // Collection properties should be read only
    public IList<Link> Relationships { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only
}
