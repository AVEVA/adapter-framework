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
using System.Reflection;
using System.Text.RegularExpressions;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.DataProtector;

public class ConfigurationProtector : IConfigurationProtector
{
    #region Public Constants

    public const string MaskedValue = "***************";

    #endregion

    #region Public Constructor

    public ConfigurationProtector(ISecretsManager secretsManager)
    {
        SecretsManager = secretsManager;
    }

    #endregion

    #region Public Fields

    public ISecretsManager SecretsManager { get; }

    #endregion

    #region Public Methods

    public static bool HasMaskedSecrets(object entry)
    {
        ThrowHelper.ThrowIfArgumentNull(entry, nameof(entry));

        var properties = GetProtectedProperties(entry.GetType());

        foreach (var property in properties)
        {
            if (string.Equals((string)property.GetValue(entry), MaskedValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryPreserveMaskedSecret(object oldEntry, object newEntry, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(newEntry, nameof(newEntry));

        errors = new List<string>();
        var properties = GetProtectedProperties(newEntry.GetType());

        foreach (var property in properties)
        {
            if (string.Equals((string)property.GetValue(newEntry), MaskedValue, StringComparison.OrdinalIgnoreCase))
            {
                if (oldEntry == null)
                {
                    errors.Add($"Secret fields can not be {MaskedValue}. The masked value can only be used when an old configuration with the same id exists.");
                    return false;
                }

                property.SetValue(newEntry, property.GetValue(oldEntry));
            }
        }

        return true;
    }

    public bool ProtectedConfigsEqual<T>(ref T configuration1, ref T configuration2) where T : class
    {
        if (configuration1 == null && configuration2 == null)
        {
            // technically they are equal
            return true;
        }

        if (configuration1 == null || configuration2 == null)
        {
            // else if one or the other is null (but not both) return false
            return false;
        }

        UnProtectSecrets(ref configuration1);
        UnProtectSecrets(ref configuration2);

        var areEqual = configuration1.Equals(configuration2);

        var protectedProperties = GetProtectedProperties(typeof(T)).ToList();

        SimpleProtectSecretProperties(configuration1, protectedProperties);
        SimpleProtectSecretProperties(configuration2, protectedProperties);

        return areEqual;
    }

    public void ProtectSecrets<T>(ref T[] configurations, string componentId, string facet) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        if (!HasProtectedProperties(typeof(T), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            ProtectSecretProperties(configuration, typeof(T).IsArray, protectedProperties, componentId, facet);
        }
    }

    public void ProtectSecrets(ref object[] configurations, Type configurationType, string componentId, string facet)
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        if (!HasProtectedProperties(configurationType.GetElementType(), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            ProtectSecretProperties(configuration, configurationType.IsArray, protectedProperties, componentId, facet);
        }
    }

    public void ProtectSecrets(ref object configuration, Type configurationType, string componentId, string facet)
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        var configurationElementType = GetConfigurationElementType(configurationType);

        var protectedProperties = GetProtectedProperties(configurationElementType);

        ProtectSecretProperties(configuration, configurationType.IsArray, protectedProperties, componentId, facet);
    }

    public void ProtectSecrets<T>(ref T configuration, string componentId, string facet) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        var protectedProperties = GetProtectedProperties(typeof(T));

        ProtectSecretProperties(configuration, typeof(T).IsArray, protectedProperties, componentId, facet);
    }

    public void UnProtectSecrets<T>(ref T[] configuration) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        if (!HasProtectedProperties(typeof(T), out _))
        {
            return;
        }

        for (var index = 0; index < configuration.Length; index++)
        {
            UnProtectSecrets(ref configuration[index]);
        }
    }

    public void UnProtectSecrets<T>(ref T configuration) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        var protectedProps = GetProtectedProperties(typeof(T));
        foreach (var property in protectedProps)
        {
            var stringValue = GetStringPropertyValue(configuration, property);

            if (!stringValue.IsNullOrEmpty())
            {
                var protectedValue = SecretsManager.Unprotect(stringValue);
                property.SetValue(configuration, protectedValue);
            }
        }
    }

    public void MaskSecrets<T>(ref T[] configurations) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));

        if (!HasProtectedProperties(typeof(T), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            MaskProtectedProperties(configuration, protectedProperties);
        }
    }

    public void MaskSecrets(ref object[] configurations, Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        if (!HasProtectedProperties(configurationType.GetElementType(), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            MaskProtectedProperties(configuration, protectedProperties);
        }
    }

    public void MaskSecrets<T>(ref T configuration) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        var protectedProperties = GetProtectedProperties(typeof(T));
        MaskProtectedProperties(configuration, protectedProperties);
    }

    public void MaskSecrets(ref object configuration, Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        configurationType = GetConfigurationElementType(configurationType);

        var protectedProperties = GetProtectedProperties(configurationType);
        MaskProtectedProperties(configuration, protectedProperties);
    }

    public void ReplaceSecretIdsWithEncryptedSecrets<T>(ref T[] configurations) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));

        if (!HasProtectedProperties(typeof(T), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            ReplaceProtectedPropertiesWithEncryptedSecret(configuration, protectedProperties);
        }
    }

    public void ReplaceSecretIdsWithEncryptedSecrets(ref object[] configurations, Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        if (!HasProtectedProperties(configurationType.GetElementType(), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            ReplaceProtectedPropertiesWithEncryptedSecret(configuration, protectedProperties);
        }
    }

    public void ReplaceSecretIdsWithEncryptedSecrets<T>(ref T configuration) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        var protectedProperties = GetProtectedProperties(typeof(T));

        ReplaceProtectedPropertiesWithEncryptedSecret(configuration, protectedProperties);
    }

    public void ReplaceSecretIdsWithEncryptedSecrets(ref object configuration, Type configurationType)
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        configurationType = GetConfigurationElementType(configurationType);
        var protectedProperties = GetProtectedProperties(configurationType);

        ReplaceProtectedPropertiesWithEncryptedSecret(configuration, protectedProperties);
    }

    public void ReconcileSecretsChange<T>(ref T[] configurations, bool commit) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));

        if (!HasProtectedProperties(typeof(T), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            ReconcileProtectedPropertiesChange(configuration, protectedProperties, commit);
        }
    }

    public void ReconcileSecretsChange(ref object[] configurations, Type configurationType, bool commit)
    {
        ThrowHelper.ThrowIfArgumentNull(configurations, nameof(configurations));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        if (!HasProtectedProperties(configurationType.GetElementType(), out var protectedProperties))
        {
            return;
        }

        foreach (var configuration in configurations)
        {
            ReconcileProtectedPropertiesChange(configuration, protectedProperties, commit);
        }
    }

    public void ReconcileSecretsChange<T>(ref T configuration, bool commit) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));

        var protectedProperties = GetProtectedProperties(typeof(T));

        ReconcileProtectedPropertiesChange(configuration, protectedProperties, commit);
    }

    public void ReconcileSecretsChange(ref object configuration, Type configurationType, bool commit)
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));
        ThrowHelper.ThrowIfArgumentNull(configurationType, nameof(configurationType));

        configurationType = GetConfigurationElementType(configurationType);
        var protectedProperties = GetProtectedProperties(configurationType);

        ReconcileProtectedPropertiesChange(configuration, protectedProperties, commit);
    }

    #endregion

    #region Private Methods

    private static string GetStringPropertyValue<T>(T configuration, PropertyInfo property)
        where T : class
    {
        var boxedPropertyValue = property.GetValue(configuration);

        return (string)boxedPropertyValue;
    }

    private static Type GetConfigurationElementType(Type configurationType)
    {
        if (configurationType.IsArray)
        {
            configurationType = configurationType.GetElementType();
        }

        return configurationType;
    }

    private static void MaskProtectedProperties<T>(T configuration, IEnumerable<PropertyInfo> protectedProperties) where T : class
    {
        foreach (var property in protectedProperties)
        {
            var stringValue = GetStringPropertyValue(configuration, property);
            if (!stringValue.IsNullOrEmpty())
            {
                if (!Regex.IsMatch(stringValue, EdgeSystemConstants.SecretIdPlaceholderPattern))
                {
                    property.SetValue(configuration, MaskedValue);
                }
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetProtectedProperties(Type configurationType)
    {
        return ConfigurationHelper.GetProtectedPropertyInfos(configurationType);
    }

    private static bool HasProtectedProperties(Type type, out IList<PropertyInfo> protectedProperties)
    {
        protectedProperties = [.. GetProtectedProperties(type)];
        return protectedProperties.Count > 0;
    }

    private void ProtectSecretProperties<T>(T configuration, bool isArrayConfiguration, IEnumerable<PropertyInfo> protectedProperties, string componentId, string facet)
        where T : class
    {
        foreach (var property in protectedProperties)
        {
            var stringValue = GetStringPropertyValue(configuration, property);
            var generatedId = ConfigurationHelper.GenerateProtectedPropertyId(configuration, isArrayConfiguration, componentId, facet, property.Name);

            if (!stringValue.IsNullOrEmpty() && !string.Equals(stringValue, MaskedValue, StringComparison.OrdinalIgnoreCase))
            {
                var protectedValue = SecretsManager.Protect(stringValue, componentId, generatedId);
                property.SetValue(configuration, protectedValue);
            }
        }
    }

    private void SimpleProtectSecretProperties<T>(T configuration, IEnumerable<PropertyInfo> protectedProperties)
        where T : class
    {
        foreach (var property in protectedProperties)
        {
            var stringValue = GetStringPropertyValue(configuration, property);
            if (!stringValue.IsNullOrEmpty())
            {
                var protectedValue = SecretsManager.Protect(stringValue);
                property.SetValue(configuration, protectedValue);
            }
        }
    }

    private void ReplaceProtectedPropertiesWithEncryptedSecret<T>(T configuration, IEnumerable<PropertyInfo> protectedProperties) where T : class
    {
        // call down to secrets manager, manager service will check if value is regex and return value from dict, otherwise it will return whatever it got
        foreach (var property in protectedProperties)
        {
            var boxedPropertyValue = property.GetValue(configuration);
            if (boxedPropertyValue == null)
            {
                continue;
            }

            var stringValue = boxedPropertyValue as string;
            if (!stringValue.IsNullOrEmpty())
            {
                property.SetValue(configuration, SecretsManager.GetProtectedString(stringValue));
            }
        }
    }

    private void ReconcileProtectedPropertiesChange<T>(T configuration, IEnumerable<PropertyInfo> protectedProperties, bool commit) where T : class
    {
        // similar to above but instead call secrets manager to commit the secrets corresponding to the regex ids in the new dictionary
        foreach (var property in protectedProperties)
        {
            var boxedPropertyValue = property.GetValue(configuration);
            if (boxedPropertyValue == null)
            {
                continue;
            }

            var stringValue = boxedPropertyValue as string;
            if (!stringValue.IsNullOrEmpty())
            {
                SecretsManager.ReconcileSecretValueChange(stringValue, commit);
            }
        }
    }

    #endregion
}
