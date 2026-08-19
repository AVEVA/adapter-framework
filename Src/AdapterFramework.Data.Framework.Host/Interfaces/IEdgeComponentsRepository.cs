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
using AdapterFramework.Data.Framework.Abstractions.Components;

namespace AdapterFramework.Data.Framework.Host.Interfaces;

public interface IEdgeComponentsRepository
{
    /// <summary>
    /// Tries to add a new <see cref="IEdgeAdapter"/> instance to the repository.
    /// </summary>
    /// <param name="adapter"><see cref="IEdgeAdapter"/> instance to add.</param>
    /// <returns>True if the <see cref="IEdgeAdapter"/> instance was added. False otherwise.</returns>
    bool TryAddAdapter(IEdgeAdapter adapter);

    /// <summary>
    /// Tries to get <see cref="IEdgeAdapter"/> instance from the repository by <paramref name="adapterId"/>.
    /// </summary>
    /// <param name="adapterId">Identifier of the requested <see cref="IEdgeAdapter"/> instance.</param>
    /// <param name="adapter">Retrieved <see cref="IEdgeAdapter"/> instance.</param>
    /// <returns>True if the <see cref="IEdgeAdapter"/> instance was found. False otherwise.</returns>
    bool TryGetAdapter(string adapterId, out IEdgeAdapter adapter);

    /// <summary>
    /// Tries to remove <see cref="IEdgeAdapter"/> instance from the repository by <paramref name="adapterId"/>.
    /// </summary>
    /// <param name="adapterId">Identifier of the <see cref="IEdgeAdapter"/> instance to remove.</param>
    /// <param name="adapter">Removed <see cref="IEdgeAdapter"/> instance.</param>
    /// <returns>True if the <see cref="IEdgeAdapter"/> was removed. False otherwise.</returns>
    bool TryRemoveAdapter(string adapterId, out IEdgeAdapter adapter);

    /// <summary>
    /// Returns <see cref="IEnumerable{T}"/> of <see cref="IEdgeAdapter"/> components present in the repository.
    /// </summary>
    /// <returns><see cref="IEnumerable{T}"/> of <see cref="IEdgeAdapter"/> instances.</returns>
    IEnumerable<IEdgeAdapter> GetAdapters();

    /// <summary>
    /// Tries to add a new <see cref="IEdgeService"/> instance to the repository.
    /// </summary>
    /// <param name="edgeService"><see cref="IEdgeService"/> instance to add.</param>
    /// <returns>True if the <see cref="IEdgeService"/> instance was added. False otherwise.</returns>
    bool TryAddEdgeService(IEdgeService edgeService);

    /// <summary>
    /// Tries to get <see cref="IEdgeService"/> instance from the repository by <paramref name="edgeServiceId"/>.
    /// </summary>
    /// <param name="edgeServiceId">Identifier of the requested <see cref="IEdgeService"/> instance.</param>
    /// <param name="edgeService">Retrieved <see cref="IEdgeService"/> instance.</param>
    /// <returns>True if the <see cref="IEdgeService"/> instance was found. False otherwise.</returns>
    bool TryGetEdgeService(string edgeServiceId, out IEdgeService edgeService);

    /// <summary>
    /// Tries to remove <see cref="IEdgeService"/> instance from the repository by <paramref name="edgeServiceId"/>.
    /// </summary>
    /// <param name="edgeServiceId">Identifier of the <see cref="IEdgeService"/> instance to remove.</param>
    /// <param name="edgeService">Removed <see cref="IEdgeService"/> instance.</param>
    /// <returns>True if the <see cref="IEdgeAdapter"/> was removed. False otherwise.</returns>
    bool TryRemoveEdgeService(string edgeServiceId, out IEdgeService edgeService);

    /// <summary>
    /// Returns <see cref="IEnumerable{T}"/> of <see cref="IEdgeService"/> components present in the repository.
    /// </summary>
    /// <returns><see cref="IEnumerable{T}"/> of <see cref="IEdgeService"/> instances.</returns>
    IEnumerable<IEdgeService> GetEdgeServices();

    /// <summary>
    /// Returns <see cref="ISinkProvider"/> instance that was added to the repository.
    /// </summary>
    /// <returns><see cref="ISinkProvider"/> instance if it was added to the repository. Null otherwise.</returns>
    ISinkProvider GetSinkProvider();

    /// <summary>
    /// Sets <see cref="ISinkProvider"/> instance in the repository.
    /// </summary>
    /// <param name="sinkProvider"><see cref="ISinkProvider"/> instance to set in the repository.</param>
    void SetSinkProvider(ISinkProvider sinkProvider);
}
