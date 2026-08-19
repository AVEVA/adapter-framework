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
using System.Threading.Tasks;

namespace AdapterFramework.Data.Framework.Abstractions.Administration;

public interface IRuntimeAdministrationRegistry
{
    /// <summary>
    /// Registers a component callback function.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="functionName">Name of the callback function.</param>
    /// <param name="callbackFunction">The callback function to register.</param>
    void RegisterComponentCallbackFunction(string componentId, string functionName, Func<Task> callbackFunction);

    /// <summary>
    /// Unregisters a component callback function.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    /// <param name="functionName">Name of the callback function to unregister.</param>
    void UnregisterComponentCallbackFunction(string componentId, string functionName);

    /// <summary>
    /// Removes all registered callback functions for the component.
    /// </summary>
    /// <param name="componentId">Component ID.</param>
    void UnregisterComponent(string componentId);

    /// <summary>
    /// Tries to get registered <see cref="Func{TResult}"/> callback function.
    /// </summary>
    /// <param name="id">Tuple of Component ID and facet to get callback function for.</param>
    /// <param name="callbackFunction">Registered callback function.</param>
    /// <returns>True when <see paramref="id"/> is found. False otherwise.</returns>
    bool TryGetCallbackFunction((string ComponentId, string Facet) id, out Func<Task> callbackFunction);

    /// <summary>
    /// Tries to get <see cref="IList{T}"/> of registered callback function names.
    /// </summary>
    /// <param name="componentId">Component ID to get registered callback function names for.</param>
    /// <param name="functionNames">Collection of registered callback function names.</param>
    /// <returns>True when <see paramref="componentId"/> is found. False Otherwise.</returns>
    bool TryGetRegisteredFunctionNames(string componentId, out IList<string> functionNames);
}
