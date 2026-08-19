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
using System.Reflection;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.MessageProcessor.Reflection;

public class PropertyValueGetter
{
    private static readonly ConcurrentDictionary<Type, PropertyValueGetter[]> _typePropertyGettersCache = new();
    private static readonly MethodInfo _innerDelegateMethodInfo = typeof(PropertyValueGetter).GetMethod(nameof(InnerDelegateFunc), BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>
    /// Property name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Get value function.
    /// </summary>
    public Func<object, object> GetValue { get; set; }

    /// <summary>
    /// Creates a collection of <see cref="PropertyValueGetter"/> instances specific to the input <paramref name="dataType"/>.
    /// </summary>
    /// <param name="dataType">Type to generate collection of property getters for.</param>
    /// <returns>Collection of <see cref="PropertyValueGetter"/> instances.</returns>
    /// <remarks>Property getters are cached.</remarks>
    public static PropertyValueGetter[] GetPropertyValueGetters(Type dataType)
    {
        ThrowHelper.ThrowIfArgumentNull(dataType, nameof(dataType));

        if (_typePropertyGettersCache.TryGetValue(dataType, out var propertyGetters))
        {
            return propertyGetters;
        }

        var typeProperties = dataType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        propertyGetters = new PropertyValueGetter[typeProperties.Length];

        for (var i = 0; i < typeProperties.Length; i++)
        {
            MethodInfo getMethod;
            if ((getMethod = typeProperties[i].GetGetMethod(true)) != null)
            {
                var getMethodDelegateType = typeof(Func<,>).MakeGenericType(typeProperties[i].DeclaringType, typeProperties[i].PropertyType);
                var getMethodDelegate = getMethod.CreateDelegate(getMethodDelegateType);
                var genericMethodInfo = _innerDelegateMethodInfo.MakeGenericMethod(typeProperties[i].DeclaringType, typeProperties[i].PropertyType);
                var resultFunction = (Func<object, object>)genericMethodInfo.Invoke(null, new object[] { getMethodDelegate });

                propertyGetters[i] = new PropertyValueGetter { GetValue = resultFunction, Name = typeProperties[i].Name };
            }
            else
            {
                throw new InvalidOperationException($"Unable to create getter for property '{typeProperties[i].Name}' for type {dataType}.");
            }
        }

        _typePropertyGettersCache.TryAdd(dataType, propertyGetters);

        return propertyGetters;
    }

    private static Func<object, object> InnerDelegateFunc<TClass, TResult>(Func<TClass, TResult> func) => instance => func((TClass)instance);
}
