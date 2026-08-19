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

namespace AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;

/// <summary>
/// This attribute describes what type of configuration class is being defined and schema version.
/// Examples: DataSource, DataSelection, Logging
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ConfigurationFacetAttribute : Attribute
{
    /// <summary>
    /// Creates a label for a configuration class that will lead to proper schema generation.
    /// </summary>
    /// <param name="componentType">The component type name such as OpcUa or Storage</param>
    /// <param name="facetName">The component facet such as DataSelection or DataSource</param>
    /// <param name="schemaVersion">The version of the schema.</param>
    /// <param name="isRequired">The flag that signifies whether the facet is required and must be always present in the configuration.</param>
    public ConfigurationFacetAttribute(string componentType, string facetName, string schemaVersion = "1.0.0", bool isRequired = true)
    {
        FacetName = facetName;
        ComponentType = componentType;
        SchemaVersion = schemaVersion;
        IsRequired = isRequired;
    }

    public string FacetName { get; }

    public string ComponentType { get; }

    public string SchemaVersion { get; }

    public bool IsRequired { get; }
}
