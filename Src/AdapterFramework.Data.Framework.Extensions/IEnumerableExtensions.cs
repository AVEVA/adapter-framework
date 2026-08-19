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
using System.Linq;

namespace AdapterFramework.Data.Framework.Extensions;

/// <summary>
/// Provides extensions to IEnumerable.
/// </summary>
public static class IEnumerableExtensions
{
    /// <summary>Determines whether a collection is empty.</summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="source">The collection to inspect.</param>
    /// <returns><c>true</c> is the collection is empty; <c>false</c> otherwise.</returns>
    public static bool IsEmpty<T>(this IEnumerable<T> source) => !source.Any();

    /// <summary>Determines whether a collection is null or empty.</summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="source">The collection to inspect.</param>
    /// <returns><c>true</c> is the collection is null or empty; <c>false</c> otherwise.</returns>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T> source)
    {
        return source == null || source.IsEmpty();
    }

    /// <summary>Converts <see cref="IEnumerable{T}"/> into <see cref="IReadOnlyList{T}"/>.</summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="source">The source collection.</param>
    /// <returns>The read-only list of items from <paramref name="source"/>.</returns>
    public static IReadOnlyList<T> ToReadOnlyList<T>(this IEnumerable<T> source) =>
        source as IReadOnlyList<T> ?? source.ToList();

    /// <summary>Converts <see cref="IEnumerable{T}"/> into <see cref="IReadOnlyCollection{T}"/>.</summary>
    /// <typeparam name="T">The type of items in the collection.</typeparam>
    /// <param name="source">The source collection.</param>
    /// <returns>The read-only collection of items from <paramref name="source"/>.</returns>
    public static IReadOnlyCollection<T> ToReadOnlyCollection<T>(this IEnumerable<T> source) =>
        source as IReadOnlyCollection<T> ?? source.ToList();
}
