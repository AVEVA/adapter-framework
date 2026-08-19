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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Registry;

public class RuntimeAdministrationRegistry : IRuntimeAdministrationRegistry
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, IList<string>> _callbackFunctionsRegistry = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly ConcurrentDictionary<(string ComponentId, string ActionName), Func<Task>> _callbackFunctions = new();

    #endregion

    #region Public Methods

    public void RegisterComponentCallbackFunction(string componentId, string functionName, Func<Task> callbackFunction)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(functionName, nameof(functionName));
        ThrowHelper.ThrowIfArgumentNull(callbackFunction, nameof(callbackFunction));

        var componentIdUpper = componentId.ToUpperInvariant();
        var actionNameUpper = functionName.ToUpperInvariant();

        if (_callbackFunctionsRegistry.TryGetValue(componentId, out var actionNames))
        {
            if (actionNames.Contains(functionName))
            {
                throw new InvalidOperationException($"Action '{functionName}' for component Id '{componentId}' has been already registered.");
            }

            actionNames.Add(functionName);
            _callbackFunctions.TryAdd((componentIdUpper, actionNameUpper), callbackFunction);
        }
        else
        {
            _callbackFunctionsRegistry.TryAdd(componentId, new List<string> { functionName });
            _callbackFunctions.TryAdd((componentIdUpper, actionNameUpper), callbackFunction);
        }
    }

    public void UnregisterComponentCallbackFunction(string componentId, string functionName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(functionName, nameof(functionName));

        var componentIdUpper = componentId.ToUpperInvariant();
        var actionNameUpper = functionName.ToUpperInvariant();

        _callbackFunctions.TryRemove((componentIdUpper, actionNameUpper), out _);

        if (_callbackFunctionsRegistry.TryGetValue(componentId, out var actionNames))
        {
            for (var i = 0; i < actionNames.Count; i++)
            {
                if (actionNames[i].Equals(functionName, StringComparison.InvariantCultureIgnoreCase))
                {
                    actionNames.RemoveAt(i);
                    break;
                }
            }
        }

        if (actionNames != null && actionNames.Count < 1)
        {
            _callbackFunctionsRegistry.TryRemove(componentId, out _);
        }
    }

    public void UnregisterComponent(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        if (_callbackFunctionsRegistry.TryGetValue(componentId, out var functionNames))
        {
            var componentIdUpper = componentId.ToUpperInvariant();

            foreach (var actionName in functionNames)
            {
                _callbackFunctions.TryRemove((componentIdUpper, actionName.ToUpperInvariant()), out _);
            }

            _callbackFunctionsRegistry.TryRemove(componentId, out _);
        }
    }

    public bool TryGetRegisteredFunctionNames(string componentId, out IList<string> functionNames)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        return _callbackFunctionsRegistry.TryGetValue(componentId, out functionNames);
    }

    public bool TryGetCallbackFunction((string ComponentId, string Facet) id, out Func<Task> callbackFunction)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id.ComponentId, nameof(id.ComponentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id.Facet, nameof(id.Facet));

        id.ComponentId = id.ComponentId.ToUpperInvariant();
        id.Facet = id.Facet.ToUpperInvariant();

        return _callbackFunctions.TryGetValue(id, out callbackFunction);
    }

    #endregion
}
