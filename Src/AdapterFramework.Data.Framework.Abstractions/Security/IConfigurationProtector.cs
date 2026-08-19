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
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;

namespace AdapterFramework.Data.Framework.Abstractions.Security;

/// <summary>
/// An interface that can provide configuration protection services.
/// </summary>
public interface IConfigurationProtector
{
    /// <summary>
    /// <see cref="ISecretsManager"/> instance.
    /// </summary>
    ISecretsManager SecretsManager { get; }

    /// <summary>
    /// Compares two configurations with protected properties and returns true if they are equal.
    /// </summary>
    /// <param name="configuration1">The first configuration with protected properties.</param>
    /// <param name="configuration2">The second configuration with protected properties.</param>
    bool ProtectedConfigsEqual<T>(ref T configuration1, ref T configuration2) where T : class;

    /// <summary>
    /// Cryptographically protects every property using <see cref="ISecretsManager"/> in the passed configuration objects array
    /// that is marked with <see cref="ProtectedAttribute"/> and has a value.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration objects array to be modified.</param>
    /// <param name="componentId">The Id of the component this configuration belongs to.</param>
    /// <param name="facet">The name of the facet this configuration belongs to.</param>
    void ProtectSecrets<T>(ref T[] configuration, string componentId, string facet) where T : class;

    /// <summary>
    /// Cryptographically protects every property using <see cref="ISecretsManager"/> in the passed configuration objects array
    /// that is marked with <see cref="ProtectedAttribute"/> and has a value.
    /// </summary>
    /// <param name="configuration">The configuration objects array to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    /// <param name="componentId">The Id of the component this configuration belongs to.</param>
    /// <param name="facet">The name of the facet this configuration belongs to.</param>
    void ProtectSecrets(ref object[] configuration, Type configurationType, string componentId, string facet);

    /// <summary>
    /// Cryptographically protects every property using <see cref="ISecretsManager"/> in the passed configuration object
    /// that is marked with <see cref="ProtectedAttribute"/> and has a value.
    /// </summary>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    /// <param name="componentId">The Id of the component this configuration belongs to.</param>
    /// <param name="facet">The name of the facet this configuration belongs to.</param>
    void ProtectSecrets(ref object configuration, Type configurationType, string componentId, string facet);

    /// <summary>
    /// Cryptographically protects every property using <see cref="ISecretsManager"/> in the passed configuration object
    /// that is marked with <see cref="ProtectedAttribute"/> and has a value.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="componentId">The Id of the component this configuration belongs to.</param>
    /// <param name="facet">The name of the facet this configuration belongs to.</param>
    void ProtectSecrets<T>(ref T configuration, string componentId, string facet) where T : class;

    /// <summary>
    /// Unprotects every property using <see cref="ISecretsManager"/> in the passed configuration objects array
    /// that is marked with <see cref="ProtectedAttribute"/> and has a value.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    void UnProtectSecrets<T>(ref T[] configuration) where T : class;

    /// <summary>
    /// Unprotects every property using <see cref="ISecretsManager"/> in the passed configuration object
    /// that is marked with <see cref="ProtectedAttribute"/> and has a value.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    void UnProtectSecrets<T>(ref T configuration) where T : class;

    /// <summary>
    /// Masks every property in the passed configuration objects array that is marked with <see cref="ProtectedAttribute"/>
    /// and has a value.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object array to be modified.</param>
    void MaskSecrets<T>(ref T[] configuration) where T : class;

    /// <summary>
    /// Masks every property in the passed configuration objects array that is marked with <see cref="ProtectedAttribute"/>
    /// and has a value.
    /// </summary>
    /// <param name="configuration">The configuration object array to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    void MaskSecrets(ref object[] configuration, Type configurationType);

    /// <summary>
    /// Masks every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// and has a value.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    void MaskSecrets<T>(ref T configuration) where T : class;

    /// <summary>
    /// Masks every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// and has a value. 
    /// </summary>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    void MaskSecrets(ref object configuration, Type configurationType);

    /// <summary>
    /// Replaces every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// with its respective encrypted secret.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    void ReplaceSecretIdsWithEncryptedSecrets<T>(ref T[] configuration) where T : class;

    /// <summary>
    /// Replaces every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// with its respective encrypted secret.
    /// </summary>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    void ReplaceSecretIdsWithEncryptedSecrets(ref object[] configuration, Type configurationType);

    /// <summary>
    /// Replaces every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// with its respective encrypted secret.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    void ReplaceSecretIdsWithEncryptedSecrets<T>(ref T configuration) where T : class;

    /// <summary>
    /// Replaces every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// with its respective encrypted secret.
    /// </summary>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    void ReplaceSecretIdsWithEncryptedSecrets(ref object configuration, Type configurationType);

    /// <summary>
    /// Checks every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// and finalizes the secrets for those values.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="commit">Whether the associated secret should persist or be thrown away.</param>
    void ReconcileSecretsChange<T>(ref T[] configuration, bool commit) where T : class;

    /// <summary>
    /// Checks every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// and finalizes the secrets for those values.
    /// </summary>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    /// <param name="commit">Whether the associated secret should persist or be thrown away.</param>
    void ReconcileSecretsChange(ref object[] configuration, Type configurationType, bool commit);

    /// <summary>
    /// Checks every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// and finalizes the secrets for those values.
    /// </summary>
    /// <typeparam name="T">The type of the configuration object.</typeparam>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="commit">Whether the associated secret should persist or be thrown away.</param>
    void ReconcileSecretsChange<T>(ref T configuration, bool commit) where T : class;

    /// <summary>
    /// Checks every property in the passed configuration object that is marked with <see cref="ProtectedAttribute"/>
    /// and finalizes the secrets for those values.
    /// </summary>
    /// <param name="configuration">The configuration object to be modified.</param>
    /// <param name="configurationType">The type of the configuration object.</param>
    /// <param name="commit">Whether the associated secret should persist or be thrown away.</param>
    void ReconcileSecretsChange(ref object configuration, Type configurationType, bool commit);
}
