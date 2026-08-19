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
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Compression;

/// <inheritdoc />
public class GZipCompressor : ICompressor
{
    private const int ReadBufferSize = 4096;

    /// <inheritdoc />
    public string Compression => "gzip";

    /// <summary>
    /// Apply gzip compression to selected data.
    /// </summary>
    /// <param name="data">The data to be compressed.</param>
    /// <returns>The compressed data.</returns>
    public byte[] Compress(byte[] data)
    {
        ThrowHelper.ThrowIfArgumentNull(data, nameof(data));

        using var stream = new MemoryStream();

        using (var zipStream = new GZipStream(stream, CompressionMode.Compress, true))
        {
            zipStream.Write(data, 0, data.Length);
        }

        var buffer = stream.GetBuffer();
        var result = new byte[stream.Length];
        Buffer.BlockCopy(buffer, 0, result, 0, (int)stream.Length);
        return result;
    }

    /// <summary>
    /// Apply compression to supplied data asynchronously.
    /// </summary>
    /// <param name="data">The data to be compressed.</param>
    /// <returns>The compressed data.</returns>
    public async Task<byte[]> CompressAsync(byte[] data)
    {
        ThrowHelper.ThrowIfArgumentNull(data, nameof(data));

        using var stream = new MemoryStream();

        using (var zipStream = new GZipStream(stream, CompressionMode.Compress, true))
        {
            await zipStream.WriteAsync(data);
        }

        var buffer = stream.GetBuffer();
        var result = new byte[stream.Length];
        Buffer.BlockCopy(buffer, 0, result, 0, (int)stream.Length);
        return result;
    }

    /// <summary>
    /// Decompress gzip-compressed data.
    /// </summary>
    /// <param name="bytes">The compressed data.</param>
    /// <returns>The decompressed data.</returns>
    public byte[] Decompress(byte[] bytes)
    {
        ThrowHelper.ThrowIfArgumentNull(bytes, nameof(bytes));

        using var stream = new MemoryStream(bytes, 0, bytes.Length);
        using var zipStream = new GZipStream(stream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();

        int bytesRead;
        var readBuffer = new byte[ReadBufferSize];
        do
        {
            bytesRead = zipStream.Read(readBuffer, 0, ReadBufferSize);

            if (bytesRead > 0)
            {
                resultStream.Write(readBuffer, 0, bytesRead);
            }
        }
        while (bytesRead > 0);

        var buffer = resultStream.GetBuffer();

        var result = new byte[resultStream.Length];

        Buffer.BlockCopy(buffer, 0, result, 0, (int)resultStream.Length);
        return result;
    }

    /// <summary>
    /// Decompress supplied data asynchronously.
    /// </summary>
    /// <param name="bytes">The compressed data.</param>
    /// <returns>The decompressed data.</returns>
    public async Task<byte[]> DecompressAsync(byte[] bytes)
    {
        ThrowHelper.ThrowIfArgumentNull(bytes, nameof(bytes));

        using var stream = new MemoryStream(bytes, 0, bytes.Length);
        using var zipStream = new GZipStream(stream, CompressionMode.Decompress);
        using var resultStream = new MemoryStream();

        int bytesRead;
        var readBuffer = new byte[ReadBufferSize];
        do
        {
            bytesRead = await zipStream.ReadAsync(readBuffer.AsMemory(0, ReadBufferSize));

            if (bytesRead > 0)
            {
                await resultStream.WriteAsync(readBuffer.AsMemory(0, bytesRead));
            }
        }
        while (bytesRead > 0);

        var buffer = resultStream.GetBuffer();
        var result = new byte[resultStream.Length];
        Buffer.BlockCopy(buffer, 0, result, 0, (int)resultStream.Length);
        return result;
    }
}
