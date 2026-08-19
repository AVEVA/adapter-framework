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
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.Abstractions.Security;

public interface ISecretsManager : IEdgeDataProtector
{
    /// <summary>
    /// Handles changes to the configuration.
    /// </summary>
    /// <param name="args">The configuration changed event arguments.</param>
    void ConfigurationChangedAction(ConfigurationChangedEventArgs args);

    /// <summary>
    /// Tries to protect and return the associated secret for the given Id and save the secret value to the Secrets configuration.
    /// </summary>
    /// <param name="secretValue">The value of a protected property, assumed to be a Secret Value or a pattern matching a Secret Id.</param>
    /// <param name="componentId">The component Id of the configuration object with a secret.</param>
    /// <param name="secretId">The Id of a secret value to be added to or updated in the Secrets configuration.</param>
    /// <returns>The protected secret</returns>
    string Protect(string secretValue, string componentId, string secretId);

    /// <summary>
    /// Checks for the secretId in the newSecrets dictionary and removes that entry, persisting into the secretsTable dictionary and disk
    /// if persist is true.
    /// </summary>
    /// <param name="secretId">The id for the secret to be finalized.</param>
    /// <param name="commit">Whether to store the secret to disk and keep in the secrets table for lookup later.</param>
    void ReconcileSecretValueChange(string secretId, bool commit = true);

    /// <summary>
    /// Returns the encrypted value of a secret for the given secretId, if found in the secrets table.
    /// </summary>
    /// <param name="secretId">The id for the encrypted secret to be returned.</param>
    /// <returns>Encrypted secret value.</returns>
    string GetProtectedString(string secretId);
}
