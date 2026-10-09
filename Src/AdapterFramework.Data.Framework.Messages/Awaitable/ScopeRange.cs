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
namespace AdapterFramework.Data.Framework.Messages.Awaitable;

/// <summary>
/// A run of consecutive values in one stream of a grouped message that belong to one scope.
/// </summary>
/// <param name="ScopeIndex">The index of the scope in <see cref="ScopeSidecar.Scopes"/>.</param>
/// <param name="Start">The position of the first value in the stream's value list.</param>
/// <param name="Count">The number of values.</param>
public readonly record struct ScopeRange(int ScopeIndex, int Start, int Count);
