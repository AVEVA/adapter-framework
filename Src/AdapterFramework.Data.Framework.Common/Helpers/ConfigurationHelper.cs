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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.Helpers;

public static class ConfigurationHelper
{
    public const string ArrayConfigurationIdPattern = "{0}.{1}.{2}.{3}"; // <componentId>.<facet>.<configurationId>.<propertyName>
    public const string NonArrayConfigurationIdPattern = "{0}.{1}.{2}"; // <componentId>.<facet>.<propertyName>
    public const string RemoveBracketsRegex = "^{{|}}$";

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    public static string GenerateProtectedPropertyId(object configurationObject, bool isArrayConfiguration, string componentId, string facet, string propertyName)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationObject, nameof(configurationObject));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(propertyName, nameof(propertyName));

        string generatedId;
        if (componentId.Equals(EdgeSystemConstants.ManagementComponentId, StringComparison.OrdinalIgnoreCase))
        {
            generatedId = ((ManagedSecretConfiguration)configurationObject).Id;
        }
        else if (isArrayConfiguration)
        {
            generatedId = string.Format(CultureInfo.InvariantCulture, ArrayConfigurationIdPattern, componentId,
                facet, GetIdProperty(configurationObject.GetType()).GetValue(configurationObject), propertyName);
        }
        else
        {
            generatedId = string.Format(CultureInfo.InvariantCulture, NonArrayConfigurationIdPattern, componentId, facet, propertyName);
        }

        return generatedId;
    }

    public static PropertyInfo GetIdProperty(Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));
        return configurationType.GetProperties().FirstOrDefault(propertyInfo => Attribute.IsDefined(propertyInfo, typeof(IdAttribute)));
    }

    public static IEnumerable<PropertyInfo> GetProtectedPropertyInfos(Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        return configurationType.GetProperties().Where(propertyInfo => Attribute.IsDefined(propertyInfo, typeof(ProtectedAttribute)));
    }

    public static object CreateObjectCopy(object configurationObject, Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        if (configurationObject == null)
        {
            return null;
        }

        var jsonSerialize = JsonSerializer.Serialize(configurationObject, configurationObject.GetType(), SerializerOptions);
        return JsonSerializer.Deserialize(jsonSerialize, configurationType, SerializerOptions);
    }

    public static string RemovePatternFromId(string secretId)
    {
        return Regex.Replace(secretId, RemoveBracketsRegex, string.Empty);
    }
}
