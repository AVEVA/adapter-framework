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
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;

/// <summary>
/// Defines SET configuration command.
/// </summary>
public interface IConfigurationSetCommand
{
    /// <summary>
    /// Previous persisted value. Protected properties' values may contain secret placeholder instead of encrypted secret.
    /// </summary>
    object OriginalValue { get; }

    /// <summary>
    /// Value to set.
    /// </summary>
    object NewValue { get; }

    /// <summary>
    /// Previous persisted value with any protected fields that had IDs now containing their encrypted secrets instead.
    /// </summary>
    object OldValue { get; }

    /// <summary>
    /// Tries to execute the <see cref="IConfigurationSetCommand"/> command.
    /// </summary>
    /// <param name="logger">Logger instance for allowing TryExecute to write error messages.</param>
    /// <param name="errors">Collection of errors encountered during the command execution.</param>
    /// <returns>True when execution succeeds. False otherwise.</returns>
    bool TryExecute(ILogger logger, out ICollection<string> errors);

    /// <summary>
    /// Tries to validate the <see cref="IConfigurationSetCommand"/> command.
    /// </summary>
    /// <param name="errors">Collection of errors encountered during the command validation.</param>
    /// <returns>True when validation succeeds. False otherwise.</returns>
    bool TryValidate(out ICollection<string> errors);

    /// <summary>
    /// Undo changes to all secret property values that are part of this command-execution.
    /// </summary>
    void RollbackChangesToSecrets();
}
