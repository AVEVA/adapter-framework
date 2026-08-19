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
using System.Threading.Tasks;
using Xunit;

namespace AdapterFramework.Data.Framework.Compression.Tests;

public class GZipCompressor_Tests
{
    [Fact]
    public void GZipCompressor_Compress_ShouldNotReturnTheSame()
    {
        var testProduct = new byte[] { 1, 2, 3 };
        var compressor = new GZipCompressor();

        var result = compressor.Compress(testProduct);

        Assert.NotNull(result);
        Assert.False(AreSameArrays(testProduct, result));
    }

    [Fact]
    public async Task GZipCompressor_CompressAsync_ShouldNotReturnTheSame()
    {
        var testProduct = new byte[] { 1, 2, 3 };
        var compressor = new GZipCompressor();

        var result = await compressor.CompressAsync(testProduct);

        Assert.NotNull(result);
        Assert.False(AreSameArrays(testProduct, result));
    }

    [Fact]
    public void GZipCompressor_Compress_InvalidInput_Throws()
    {
        byte[] testProduct = null;
        var compressor = new GZipCompressor();

        Assert.Throws<ArgumentNullException>(() => compressor.Compress(testProduct));
    }

    [Fact]
    public async Task GZipCompressor_CompressAsync_InvalidInput_Throws()
    {
        byte[] testProduct = null;
        var compressor = new GZipCompressor();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await compressor.CompressAsync(testProduct));
    }

    [Fact]
    public void GZipCompressor_Decompress_ShouldReturnTheSame()
    {
        var testProduct = new byte[] { 1, 2, 3, 4, 4 };
        var compressor = new GZipCompressor();
        var compressed = compressor.Compress(testProduct);

        Assert.NotNull(compressed);
        Assert.False(AreSameArrays(testProduct, compressed));

        var decompressed = compressor.Decompress(compressed);

        Assert.NotNull(decompressed);
        Assert.True(AreSameArrays(testProduct, decompressed));
    }

    [Fact]
    public async Task GZipCompressor_DecompressAsync_ShouldReturnTheSame()
    {
        var testProduct = new byte[] { 1, 2, 3, 4, 4 };

        var compressor = new GZipCompressor();
        var compressed = await compressor.CompressAsync(testProduct);

        Assert.NotNull(compressed);
        Assert.False(AreSameArrays(testProduct, compressed));

        var decompressed = await compressor.DecompressAsync(compressed);

        Assert.NotNull(decompressed);
        Assert.True(AreSameArrays(testProduct, decompressed));
    }

    [Fact]
    public void GZipCompressor_Compress_ShouldReturnTheSameEmpty()
    {
        var testProduct = Array.Empty<byte>();
        var compressor = new GZipCompressor();
        var compressed = compressor.Compress(testProduct);

        Assert.NotNull(compressed);
        
        // .NET 10 change - Empty byte[] compression results in empty byte[], hence compressed and uncompressed data should be same, empty.
        Assert.True(AreSameArrays(testProduct, compressed));
    }

    [Fact]
    public async Task GZipCompressor_CompressAsync_ShouldReturnTheSameEmpty()
    {
        var testProduct = Array.Empty<byte>();
        var compressor = new GZipCompressor();
        var compressed = await compressor.CompressAsync(testProduct);

        Assert.NotNull(compressed);

        // .NET 10 change - Empty byte[] compression results in empty byte[], hence compressed and uncompressed data should be same, empty.
        Assert.True(AreSameArrays(testProduct, compressed));
    }

    [Fact]
    public void GZipCompressor_Decompress_ShouldReturnTheSameEmpty()
    {
        var testProduct = Array.Empty<byte>();
        var compressor = new GZipCompressor();
        var compressed = compressor.Compress(testProduct);
        var decompressed = compressor.Decompress(compressed);

        Assert.NotNull(decompressed);
        Assert.Empty(decompressed);
        Assert.True(AreSameArrays(testProduct, decompressed));
    }

    [Fact]
    public async Task GZipCompressor_DecompressAsync_ShouldReturnTheSameEmpty()
    {
        var testProduct = Array.Empty<byte>();
        var compressor = new GZipCompressor();
        var compressed = await compressor.CompressAsync(testProduct);
        var decompressed = compressor.Decompress(compressed);

        Assert.NotNull(decompressed);
        Assert.Empty(decompressed);
        Assert.True(AreSameArrays(testProduct, decompressed));
    }

    [Fact]
    public async Task GZipCompressor_DecompressAsync_InvalidInput_Throws()
    {
        byte[] testProduct = null;
        var compressor = new GZipCompressor();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await compressor.DecompressAsync(testProduct));
    }

    [Fact]
    public void GZipCompressor_Decompress_InvalidInput_Throws()
    {
        byte[] testProduct = null;
        var compressor = new GZipCompressor();

        Assert.Throws<ArgumentNullException>(() => compressor.Decompress(testProduct));
    }

    private static bool AreSameArrays(byte[] array1, byte[] array2)
    {
        if (array1.Length != array2.Length)
        {
            return false;
        }

        for (int i = 0; i < array1.Length; i++)
        {
            if (array1[i] != array2[i])
            {
                return false;
            }
        }

        return true;
    }
}
