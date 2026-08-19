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

namespace AdapterFramework.Data.Framework.Extensions;

/// <summary>
/// Provides helper methods to throw exceptions.
/// </summary>
public static class ThrowHelper
{
    /// <summary>Makes sure the argument value is not null.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the argument value is null.</exception>
    /// <param name="value">The argument value.</param>
    /// <param name="argumentName">The argument name.</param>
    public static void ThrowIfArgumentNull(object value, string argumentName)
    {
        if (value == null)
        {
            throw new ArgumentNullException(argumentName);
        }
    }

    /// <summary>Makes sure the string argument value is not null or empty.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the string argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the string argument is empty.</exception>
    /// <param name="value">The argument value.</param>
    /// <param name="argumentName">The argument name.</param>
    public static void ThrowIfArgumentNullOrEmpty<T>(IEnumerable<T> value, string argumentName)
    {
        ThrowIfArgumentNull(value, argumentName);
        ThrowIfArgumentEmpty(value, argumentName);
    }

    /// <summary>Makes sure the string argument is not null, empty or only white space.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the string argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the string argument is empty.</exception>
    /// <exception cref="ArgumentException">Thrown when string is only white space.</exception>
    /// <param name="value">The argument value.</param>
    /// <param name="argumentName">The argument name.</param>
    public static void ThrowIfArgumentNullEmptyOrWhiteSpace(string value, string argumentName)
    {
        ThrowIfArgumentNullOrEmpty(value, argumentName);
        ThrowIfArgumentWhiteSpace(value, argumentName);
    }

    /// <summary>Makes sure the string argument value is not empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the string argument is empty.</exception>
    /// <param name="value">The argument value.</param>
    /// <param name="argumentName">The argument name.</param>
    private static void ThrowIfArgumentEmpty<T>(IEnumerable<T> value, string argumentName)
    {
        if (value.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(argumentName);
        }
    }

    /// <summary>Makes sure the string argument value is not only white space.</summary>
    /// <exception cref="ArgumentException">Thrown when string is only white space.</exception>
    /// <param name="value">The argument value.</param>
    /// <param name="argumentName">The argument name.</param>
    private static void ThrowIfArgumentWhiteSpace(string value, string argumentName)
    {
        if (value.Trim().Length == 0)
        {
            throw new ArgumentException("The argument cannot contain only white space.", argumentName);
        }
    }
}
