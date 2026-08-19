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
namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <summary>
/// An interface that defines the required properties and methods to implement in a serializer.
/// </summary>
public interface ISerializer
{
    /// <summary>
    /// Gets the serialization format.
    /// </summary>
    string Format { get; }

    /// <summary>
    /// Serialize an object to a byte array.
    /// </summary>
    /// <param name="objectToBeSerialized">The object to be serialized.</param>
    /// <returns>The serialized object as a byte array.</returns>
    byte[] Serialize(object objectToBeSerialized);

    /// <summary>
    /// Serialized an object of type <typeparamref name="T"/> to a byte array.
    /// </summary>
    /// <typeparam name="T">The type of the object to be serialized.</typeparam>
    /// <param name="objectToBeSerialized">The object to be serialized.</param>
    /// <returns>The serialized object as a byte array.</returns>
    byte[] Serialize<T>(T objectToBeSerialized);

    /// <summary>
    /// Deserialize a byte array to an object.
    /// </summary>
    /// <param name="bytes">The byte array to be deserialized.</param>
    /// <returns>The deserialized object.</returns>
    object Deserialize(byte[] bytes);

    /// <summary>
    /// Deserialize a byte array to an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the deserialized object.</typeparam>
    /// <param name="bytes">The byte array to be deserialized.</param>
    /// <returns>The deserialized object.</returns>
    T Deserialize<T>(byte[] bytes);

    /// <summary>
    /// Deserialize a string to an object.
    /// </summary>
    /// <param name="content">The string to be deserialized.</param>
    /// <returns>The deserialized object.</returns>
    object Deserialize(string content);

    /// <summary>
    /// Deserialize a string to an object of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of the deserialized object.</typeparam>
    /// <param name="content">The string to be deserialized.</param>
    /// <returns>The deserialized object.</returns>
    T Deserialize<T>(string content);
}
