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
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.DataModel.Extensions;

/// <summary>
/// Defines data model type extensions.
/// </summary>
public static class TypeExtensions
{
    /// <summary>
    /// Returns the matching <see cref="PropertyDefinition"/> for the Type value passed in.
    /// </summary>
    /// <param name="valueType">Supplied Type value.</param>
    /// <returns>The <see cref="PropertyDefinition"/> for the supplied Type or null if the Type was not found.</returns>
    public static PropertyDefinition ToPropertyDefinition(this Type valueType)
    {
        ThrowHelper.ThrowIfArgumentNull(valueType, nameof(valueType));

        if (valueType.IsArray)
        {
            var elementType = valueType.GetElementType();
            var itemDefinition = elementType.ToPropertyDefinition();
            
            if (itemDefinition != null)
            {
                return new PropertyDefinition 
                { 
                    Type = Tokens.ArrayToken,
                    Items = itemDefinition,
                };
            }
            
            return null;
        }

        if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return Nullable.GetUnderlyingType(valueType).Name switch
            {
                nameof(Boolean) => new NullablePropertyDefinition { Type = new string[] { Tokens.BooleanToken, Tokens.NullToken } },
                nameof(UInt16) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.UInt16Token },
                nameof(Byte) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.Int16Token },
                nameof(SByte) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.Int16Token },
                nameof(Int16) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.Int16Token },
                nameof(Int32) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.Int32Token },
                nameof(UInt32) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.UInt32Token },
                nameof(Int64) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.Int64Token },
                nameof(UInt64) => new NullablePropertyDefinition { Type = new string[] { Tokens.IntegerToken, Tokens.NullToken }, Format = Tokens.UInt64Token },
                nameof(Double) => new NullablePropertyDefinition { Type = new string[] { Tokens.NumberToken, Tokens.NullToken }, Format = Tokens.Float64Token },
                nameof(Single) => new NullablePropertyDefinition { Type = new string[] { Tokens.NumberToken, Tokens.NullToken }, Format = Tokens.Float32Token },
                nameof(Decimal) => new NullablePropertyDefinition { Type = new string[] { Tokens.NumberToken, Tokens.NullToken }, Format = Tokens.Float64Token },
                nameof(DateTime) => new NullablePropertyDefinition { Type = new string[] { Tokens.StringToken, Tokens.NullToken }, Format = Tokens.DateTimeToken },
                nameof(String) => new NullablePropertyDefinition { Type = new string[] { Tokens.StringToken, Tokens.NullToken } },
                _ => null,
            };
        }

        return valueType.Name switch
        {
            nameof(Boolean) => new PropertyDefinition { Type = Tokens.BooleanToken },
            nameof(UInt16) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.UInt16Token },
            nameof(Byte) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.Int16Token },
            nameof(SByte) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.Int16Token },
            nameof(Int16) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.Int16Token },
            nameof(Int32) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.Int32Token },
            nameof(UInt32) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.UInt32Token },
            nameof(Int64) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.Int64Token },
            nameof(UInt64) => new PropertyDefinition { Type = Tokens.IntegerToken, Format = Tokens.UInt64Token },
            nameof(Double) => new PropertyDefinition { Type = Tokens.NumberToken, Format = Tokens.Float64Token },
            nameof(Single) => new PropertyDefinition { Type = Tokens.NumberToken, Format = Tokens.Float32Token },
            nameof(Decimal) => new PropertyDefinition { Type = Tokens.NumberToken, Format = Tokens.Float64Token },
            nameof(DateTime) => new PropertyDefinition { Type = Tokens.StringToken, Format = Tokens.DateTimeToken },
            nameof(String) => new PropertyDefinition { Type = Tokens.StringToken },
            _ => null,
        };
    }
}
