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
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public class Serializer_Tests : IDisposable
{
    private const string TestFile = "testfile";
    private readonly FileStream _stream;
    private readonly Serializer _serializer;
    private bool _disposed;

    public Serializer_Tests()
    {
        if (File.Exists(TestFile))
        {
            File.Delete(TestFile);
        }

        _stream = new FileStream(TestFile, FileMode.Create, FileAccess.ReadWrite);
        _serializer = new Serializer();
    }

    #region QueueState

    [Fact]
    public void Serializer_DeserializeQueueState_NoDataInStream_FreshQueueStateReturned()
    {
        // Ensure the file is empty
        Assert.Equal(0, _stream.Length);

        var qs = _serializer.DeserializeQueueState(_stream);

        Assert.Equal(0, qs.ReaderFileNumber);
        Assert.Equal(0, qs.ReaderPosition);
        Assert.Equal(0, qs.WriterFileNumber);
        Assert.Equal(0, qs.WriterPosition);
    }

    [Fact]
    public void Serializer_SerializeDeserializeQueueState_Success()
    {
        var qs = GenerateAQueueState();

        _serializer.SerializeQueueState(_stream, qs);
        
        // Set stream to 0 so we read from the beginning of the file
        _stream.Position = 0;

        var deserializedQs = _serializer.DeserializeQueueState(_stream);

        Assert.NotEqual(qs, deserializedQs); // Ensure no reference equality
        Assert.True(AllPropsEqual(qs, deserializedQs)); // Ensure all fields equal
    }

    [Fact]
    public void Serializer_SerializeWriterAndReaderState_BothSerialized()
    {
        var qs = GenerateAQueueState();

        _serializer.SerializeWriterState(_stream, qs);
        _stream.Position = 0;
        _serializer.SerializeReaderState(_stream, qs);

        // Set stream to 0 so we read from the beginning of the file
        _stream.Position = 0;

        var deserializedQs = _serializer.DeserializeQueueState(_stream);

        Assert.NotEqual(qs, deserializedQs); // Ensure no reference equality
        Assert.True(AllPropsEqual(qs, deserializedQs)); // Ensure all fields equal
    }

    [Fact]
    public void Serializer_SerializeWriterState_WriterStateSerializedReaderStateNotSerialized()
    {
        var qs1 = GenerateAQueueState();

        _serializer.SerializeQueueState(_stream, qs1);

        var qs2 = GenerateADifferentQueueState();
        Assert.True(NoPropsEqual(qs1, qs2));

        _stream.Position = 0;
        _serializer.SerializeWriterState(_stream, qs2);

        _stream.Position = 0;
        var deserializedQs = _serializer.DeserializeQueueState(_stream);

        // The deserialized queuestate should have the reader state of qs1 and the writer state of qs2
        Assert.True(ReaderPropsEqual(qs1, deserializedQs));
        Assert.True(WriterPropsEqual(qs2, deserializedQs));
    }

    [Fact]
    public void Serializer_SerializereaderState_ReaderStateSerializedWriterStateNotSerialized()
    {
        var qs1 = GenerateAQueueState();

        _serializer.SerializeQueueState(_stream, qs1);

        var qs2 = GenerateADifferentQueueState();
        Assert.True(NoPropsEqual(qs1, qs2));

        _stream.Position = 0;
        _serializer.SerializeReaderState(_stream, qs2);

        _stream.Position = 0;
        var deserializedQs = _serializer.DeserializeQueueState(_stream);

        // The deserialized queuestate should have the writer state of qs1 and the reader state of qs2
        Assert.True(WriterPropsEqual(qs1, deserializedQs));
        Assert.True(ReaderPropsEqual(qs2, deserializedQs));
    }

    #endregion

    #region DataItem

    [Theory]
    [InlineData(DataItemVersion.V1)]
    [InlineData(DataItemVersion.V2)]
    [InlineData(DataItemVersion.V3)]
    [InlineData(DataItemVersion.V4)]
    public void Serializer_SerializeDeserializeDataItem_Success(DataItemVersion dataItemVersion)
    {
        var rnd = new Random();
        var buffer = new byte[24];
        rnd.NextBytes(buffer);

        var item = new DataItem(dataItemVersion, buffer);
        _serializer.SerializeDataItem(_stream, item);

        _stream.Position = 0;
        var deserializedItem = _serializer.DeserializeDataItem(_stream);

        Assert.NotNull(deserializedItem);
        Assert.Equal(item.Version, deserializedItem.Version);
        Assert.Equal(item.Data, deserializedItem.Data);
    }

    [Fact]
    public void Serializer_DeserializeDataItem_FileNotLongEnough_ResetsStreamPositionReturnsNull()
    {
        // Test empty stream
        Assert.Equal(0, _stream.Length);
        Assert.Equal(0, _stream.Position);

        var item = _serializer.DeserializeDataItem(_stream);

        Assert.Null(item);
        Assert.Equal(0, _stream.Position);

        // Test stream with some data and not at position 0
        var randomBytes = new byte[] { 12, 23, 34, 45, 96 };
        _stream.Write(randomBytes, 0, randomBytes.Length);
        _stream.Flush(true);
        _stream.Position = 1;

        item = _serializer.DeserializeDataItem(_stream);

        Assert.Null(item);
        Assert.Equal(1, _stream.Position);
    }

    [Fact]
    public void Serializer_DeserializeDataItem_InvalidStartBytes_ThrowsDataRecordExceptionAndResetsStreamPosition()
    {
        var dataItem = new DataItem(DataItemVersion.V2, new byte[24]);
        _serializer.SerializeDataItem(_stream, dataItem);

        // The first 8 bytes of the data item are header sequence, let's corrupt a couple of the bytes
        _stream.Position = 4;
        _stream.WriteByte(0);
        _stream.WriteByte(0);
        _stream.Flush(true);

        bool dataRecordExceptionThrown = false;
        _stream.Position = 0;
        try
        {
            var item = _serializer.DeserializeDataItem(_stream);
        }
        catch (DataRecordException)
        {
            dataRecordExceptionThrown = true;
        }

        Assert.True(dataRecordExceptionThrown);
        Assert.Equal(0, _stream.Position);
    }

    [Fact]
    public void Serializer_DeserializeDataItem_CorruptedData_ThrowsDataRecordExceptionAndResetsStreamPosition()
    {
        var dataItem = new DataItem(DataItemVersion.V2, new byte[24]);
        _serializer.SerializeDataItem(_stream, dataItem);

        // There are 12 bytes in the header (8 bytes for start sequence and 4 for length), and 24 bytes in the data item
        // So bytes 12-35 will be the data bytes.
        // Let's corrupt a couple of them
        _stream.Position = 15;
        _stream.WriteByte(10);
        _stream.WriteByte(12);
        _stream.Flush(true);

        bool dataRecordExceptionThrown = false;
        _stream.Position = 0;
        try
        {
            var item = _serializer.DeserializeDataItem(_stream);
        }
        catch (DataRecordException)
        {
            dataRecordExceptionThrown = true;
        }

        Assert.True(dataRecordExceptionThrown);
        Assert.Equal(0, _stream.Position);
    }

    [Fact]
    public void Serializer_DeserializeDataItem_InvalidFooterBytes_ThrowsDataRecordExceptionAndResetsStreamPosition()
    {
        var dataItem = new DataItem(DataItemVersion.V1, new byte[24]);
        _serializer.SerializeDataItem(_stream, dataItem);

        // There are 12 bytes in the header (8 bytes for start sequence and 4 for length), and 24 bytes in the data item
        // So bytes 36-39 will be the 4 CRC32 bytes.
        // Let's corrupt a few of them
        _stream.Position = 36;
        _stream.WriteByte(0);
        _stream.WriteByte(0);
        _stream.Flush(true);

        bool dataRecordExceptionThrown = false;
        _stream.Position = 0;
        try
        {
            var item = _serializer.DeserializeDataItem(_stream);
        }
        catch (DataRecordException)
        {
            dataRecordExceptionThrown = true;
        }

        Assert.True(dataRecordExceptionThrown);
        Assert.Equal(0, _stream.Position);
    }

    [Fact]
    public void Serializer_TryDeserializeNextValidDataItem_FindsDataItemAtStreamPosition()
    {
        var item = GenerateRandomDataItem(24);
        _serializer.SerializeDataItem(_stream, item);

        _stream.Position = 0;
        Assert.True(_serializer.TryDeserializeNextValidDataItem(_stream, out var deserializedItem));
        Assert.Equal(item.Data, deserializedItem.Data);
    }

    [Fact]
    public void Serializer_TryDeserializeNextValidDataItem_FindsDataItemFurtherInStream()
    {
        var item = GenerateRandomDataItem(24);
        _stream.SetLength(100);
        _stream.Position = 40;
        _serializer.SerializeDataItem(_stream, item);

        _stream.Position = 0;
        Assert.True(_serializer.TryDeserializeNextValidDataItem(_stream, out var deserializedItem));
        Assert.Equal(item.Data, deserializedItem.Data);
    }

    [Fact]
    public void Serializer_TryDeserializeNextValidDataItem_NoValidDataItem_ReturnsFalseResetsStreamPosition()
    {
        _stream.SetLength(100);

        _stream.Position = 0;
        Assert.False(_serializer.TryDeserializeNextValidDataItem(_stream, out var _));
        Assert.Equal(0, _stream.Position);
    }

    [Fact]
    public void Serializer_TryDeserializeNextValidDataItem_InvalidStartBytes_ReturnsFalseResetsStreamPosition()
    {
        var item = GenerateRandomDataItem(24, DataItemVersion.V1);
        _serializer.SerializeDataItem(_stream, item);

        // The first 8 bytes of the data item are header sequence, let's corrupt a couple of the bytes
        _stream.Position = 4;
        _stream.WriteByte(0);
        _stream.WriteByte(0);
        _stream.Flush(true);
        _stream.Position = 0;
        Assert.False(_serializer.TryDeserializeNextValidDataItem(_stream, out var _));
        Assert.Equal(0, _stream.Position);
    }

    #endregion

    #region IDisposable pattern

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _stream?.Dispose();

                if (File.Exists(TestFile))
                {
                    File.Delete(TestFile);
                }
            }

            _disposed = true;
        }
    }

    #endregion

    #region Helpers

    private static DataItem GenerateRandomDataItem(int size, DataItemVersion dataItemVersion = DataItemVersion.V1)
    {
        var rnd = new Random();
        var buffer = new byte[size];
        rnd.NextBytes(buffer);
        return new DataItem(dataItemVersion, buffer);
    }

    private static QueueState GenerateAQueueState()
    {
        return new QueueState(123, 432, 92, 75);
    }

    /// <summary>
    /// Generates a Queue State where all properties are different than that returned by <see cref="GenerateAQueueState"/>
    /// </summary>
    private static QueueState GenerateADifferentQueueState()
    {
        return new QueueState(78, 99, 43, 32);
    }

    private static bool NoPropsEqual(QueueState qs1, QueueState qs2)
    {
        return qs1.ReaderFileNumber != qs2.ReaderFileNumber
               && qs1.ReaderPosition != qs2.ReaderPosition
               && qs1.WriterFileNumber != qs2.WriterFileNumber
               && qs1.WriterPosition != qs2.WriterPosition;
    }

    private static bool AllPropsEqual(QueueState qs1, QueueState qs2)
    {
        return qs1.ReaderFileNumber == qs2.ReaderFileNumber
               && qs1.ReaderPosition == qs2.ReaderPosition
               && qs1.WriterFileNumber == qs2.WriterFileNumber
               && qs1.WriterPosition == qs2.WriterPosition;
    }

    private static bool ReaderPropsEqual(QueueState qs1, QueueState qs2)
    {
        return qs1.ReaderFileNumber == qs2.ReaderFileNumber
               && qs1.ReaderPosition == qs2.ReaderPosition;
    }

    private static bool WriterPropsEqual(QueueState qs1, QueueState qs2)
    {
        return qs1.WriterFileNumber == qs2.WriterFileNumber
               && qs1.WriterPosition == qs2.WriterPosition;
    }

    #endregion
}
