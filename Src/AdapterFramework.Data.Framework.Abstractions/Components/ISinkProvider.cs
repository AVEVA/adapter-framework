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
using System.Threading.Tasks;

namespace AdapterFramework.Data.Framework.Abstractions.Components;

/// <inheritdoc />
/// <summary>
/// Defines methods that Sink provider component must implement.
/// </summary>
public interface ISinkProvider : IEdgeComponent
{
    /// <summary>
    /// Asynchronously initializes <see cref="ISinkProvider"/> component.
    /// </summary>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Asynchronously starts <see cref="ISinkProvider"/> component.
    /// </summary>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StartAsync();

    /// <summary>
    /// Asynchronously stops <see cref="ISinkProvider"/> component.
    /// </summary>
    /// <returns>The asynchronously running task for the operation.</returns>
    Task StopAsync();

    /// <summary>
    /// Signals the <see cref="ISinkProvider"/> component to resend its health types, streams and static data.
    /// </summary>
    void ResendHealthMetadata();
}
