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

namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <summary>
/// An interface for compressor that does the data compression.
/// </summary>
public interface ICompressor
{
    /// <summary>
    /// Gets the compression format.
    /// </summary>
    string Compression { get; }

    /// <summary>
    /// Synchronously compress the byte array named <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The byte array to be compressed.</param>
    /// <returns>The compressed byte array.</returns>
    byte[] Compress(byte[] data);

    /// <summary>
    /// Asynchronously compress the byte array named <paramref name="data"/>.
    /// </summary>
    /// <param name="data">The byte array to be compressed.</param>
    /// <returns>The compressed byte array.</returns>
    Task<byte[]> CompressAsync(byte[] data);

    /// <summary>
    /// Synchronously decompress the byte array named <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">The byte array to be decompressed.</param>
    /// <returns>The decompressed byte array.</returns>
    byte[] Decompress(byte[] bytes);

    /// <summary>
    /// Asynchronously decompress the byte array named <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">The byte array to be decompressed.</param>
    /// <returns>The decompressed byte array.</returns>
    Task<byte[]> DecompressAsync(byte[] bytes);
}
