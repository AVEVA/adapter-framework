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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using AdapterFramework.Data.DataModel.Converters;
using AdapterFramework.Data.DataModel.Extensions;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object representing a dynamic OMF Type message.
/// </summary>
public class DynamicDataType : DataType
{
    private readonly OmfVersion _omfVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicDataType"/> with the default values.
    /// </summary>
    public DynamicDataType()
    {
        Type = Tokens.ObjectToken;
        _omfVersion = OmfVersion.Omf12;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicDataType"/> that includes the <paramref name="typeId"/>, <paramref name="typeName"/> and <paramref name="properties"/>.
    /// </summary>
    /// <param name="typeId">ID of the type.</param>
    /// <param name="typeName">Name of the type.</param>
    /// <param name="properties">Dictionary of property names, types and flags signifying if the given property should be marked as Index or Quality. </param>
    /// <param name="omfVersion">The target OMF version.</param>
    /// <remarks>At least one property must have IsProperty set to true. A property cannot have both isIndex and IsQuality true.</remarks>
    public DynamicDataType(string typeId, string typeName, IReadOnlyDictionary<string, (Type PropertyType, bool IsIndex, bool IsQuality, string QualitySchema)> properties, OmfVersion omfVersion = OmfVersion.Omf12) : this()
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
        ThrowHelper.ThrowIfArgumentNull(properties, nameof(properties));

        _omfVersion = omfVersion;

        var propertyDefinitions = new Dictionary<string, PropertyDefinition>();

        var indexPropertyIncluded = false;
        foreach (var (propertyName, (propertyType, isIndex, isQuality, qualitySchema)) in properties)
        {
            var propertyDefinition = propertyType.ToPropertyDefinition() ?? throw new NotSupportedException($"Unsupported type received for property '{propertyName}' with type of {propertyType}.");
            if (isIndex)
            {
                propertyDefinition.IsIndex = true;
                indexPropertyIncluded = true;
            }

            if (isQuality || !string.IsNullOrWhiteSpace(qualitySchema))
            {
                if (isIndex)
                {
                    throw new NotSupportedException("A property may not have both isIndex and isQuality set to true.");
                }

                if (omfVersion == OmfVersion.Omf12)
                {
                    propertyDefinition.IsQuality = true;
                }
                else 
                {
                    propertyDefinition.QualitySchema = qualitySchema;
                }
            }

            propertyDefinitions.Add(propertyName, propertyDefinition);
        }

        if (!indexPropertyIncluded)
        {
            throw new InvalidOperationException("At least one property must be marked as Index.");
        }

        Id = typeId;
        Name = typeName;
        Properties = propertyDefinitions;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicDataType"/> class.
    /// </summary>
    /// <param name="typeId">ID of the type.</param>
    /// <param name="typeName">Name of the type.</param>
    /// <param name="properties">Dictionary of property definitions to be included in the type.</param>
    /// <param name="omfVersion">The target OMF version.</param>
    public DynamicDataType(string typeId, string typeName, IReadOnlyDictionary<string, PropertyDefinition> properties, OmfVersion omfVersion = OmfVersion.Omf12) : this()
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
        ThrowHelper.ThrowIfArgumentNull(properties, nameof(properties));

        _omfVersion = omfVersion;

        if (!properties.Values.Any(x => (bool)x.IsIndex))
        {
            throw new InvalidOperationException("At least one property must be marked as Index.");
        }

        Id = typeId;
        Name = typeName;
        Properties = new Dictionary<string, PropertyDefinition>(properties);
    }

    /// <inheritdoc/>
    [JsonPropertyOrder(2)]
    [JsonPropertyName(Tokens.Classification)]
    [JsonConverter(typeof(RequiredPropertyConverter<string>))]
    public override string Classification => _omfVersion switch
    {
        OmfVersion.Omf12 => Tokens.Dynamic,
        OmfVersion.Omf20 => Tokens.StreamingData,
        _ => throw new NotSupportedException($"OMF version {_omfVersion} is not supported."),
    };
}
