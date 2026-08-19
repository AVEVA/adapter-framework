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
using System.Threading;

namespace AdapterFramework.Data.Framework.Abstractions.Services;

public interface ICancellationTokenService : IDisposable
{
    /// <summary>
    /// Returns a <see cref="CancellationToken"/> for the given component.
    /// Creates a <see cref="CancellationTokenSource"/> if it does not exist.
    /// </summary>
    /// <param name="componentId">The id of the component.</param>
    /// <returns>CancellationToken corresponding to the <paramref name="componentId"/>.</returns>
    /// <remarks>A <see cref="CancellationToken"/> will still be returned even if the <see cref="CancellationTokenSource"/> has been canceled.</remarks>
    CancellationToken GetCancellationToken(string componentId);

    /// <summary>
    /// Cancellation is called on the <see cref="CancellationTokenSource"/> assigned to the component.
    /// </summary>
    /// <param name="componentId">The id of the component.</param>
    void RequestCancellation(string componentId);
}
