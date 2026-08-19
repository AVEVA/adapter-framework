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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.ConfigurationProvider;

public static class ConfigurationProcessor
{
    public static bool TryExecuteCommand(IConfigurationGetCommand command, out object value, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(command, nameof(command));

        return command.TryExecute(out value, out errors);
    }

    public static bool TryExecuteCommand(IConfigurationSetCommand command, Action<ConfigurationChangedEventArgs> callbackAction, ILogger logger, out ICollection<string> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(command, nameof(command));

        var success = command.TryExecute(logger, out errors);

        if (success)
        {
            try
            {
                callbackAction?.Invoke(new ConfigurationChangedEventArgs(command.OldValue, command.NewValue));
            }
            catch (Exception ex)
            {
                errors.Add($"Unexpected error encountered while executing callback action. {ex.GetExceptionTypeAndMessages()}.");
                return false;
            }
        }

        return success;
    }

    public static bool TryExecuteCommand(IConfigurationSetPatchCommand command, Action<ConfigurationChangedEventArgs> callbackAction, ILogger logger, out ICollection<(int StatusCode, string ErrorMessage)> errors)
    {
        ThrowHelper.ThrowIfArgumentNull(command, nameof(command));

        var success = command.TryExecute(logger, out errors);

        if (success)
        {
            try
            {
                callbackAction?.Invoke(new ConfigurationChangedEventArgs(command.OldValue, command.NewValue));
            }
            catch (Exception ex)
            {
                errors.Add((StatusCodes.Status400BadRequest, $"Unexpected error encountered while executing callback action. {ex.GetExceptionTypeAndMessages()}."));
                return false;
            }
        }

        return success;
    }
}
