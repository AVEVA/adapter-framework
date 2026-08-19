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
using System.Linq;
using System.Text;
using Force.Crc32;
using AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

internal class Serializer : ISerializer
{
    public const int DataItemSerializedOverhead = 16; // 8 Bytes for start marker, 4 for DataLength, 4 bytes for CRC32 at the end.
    private const int ReaderStateSize = 12; // Size in bytes of serialized reader state - 4 bytes for Int32 ReaderFileNumber, 8 bytes for Int64 ReaderFilePosition.
    private const int DataItemStartByteCount = 8;
    private static readonly byte[] _dataItemStartBytesV1 = new byte[DataItemStartByteCount] { 0x81, 0x4c, 0xed, 0x2f, 0x2f, 0x37, 0x4a, 0x15 };
    private static readonly byte[] _dataItemStartBytesV2 = new byte[DataItemStartByteCount] { 0x42, 0x1c, 0x4b, 0x99, 0x8e, 0x7d, 0x11, 0x41 };
    private static readonly byte[] _dataItemStartBytesV3 = new byte[DataItemStartByteCount] { 0x32, 0x4c, 0x42, 0x92, 0x23, 0x32, 0x13, 0x42 };
    private static readonly Encoding _payloadEncoding = new UTF8Encoding(false);

    #region Public Methods

    #region QueueState

    public void SerializeQueueState(Stream stream, QueueState queueState)
    {
        using var binaryWriter = new BinaryWriter(stream, _payloadEncoding, true);
        binaryWriter.Write(queueState.ReaderFileNumber);
        binaryWriter.Write(queueState.ReaderPosition);
        binaryWriter.Write(queueState.WriterFileNumber);
        binaryWriter.Write(queueState.WriterPosition);
    }

    public void SerializeReaderState(Stream stream, QueueState queueState)
    {
        using var binaryWriter = new BinaryWriter(stream, _payloadEncoding, true);
        binaryWriter.Write(queueState.ReaderFileNumber);
        binaryWriter.Write(queueState.ReaderPosition);
    }

    public void SerializeWriterState(Stream stream, QueueState queueState)
    {
        // We only want to serialize the writer portion, so advance the stream past the reader portion
        stream.Position += ReaderStateSize;

        using var binaryWriter = new BinaryWriter(stream, _payloadEncoding, true);
        binaryWriter.Write(queueState.WriterFileNumber);
        binaryWriter.Write(queueState.WriterPosition);
    }

    public QueueState DeserializeQueueState(Stream stream)
    {
        try
        {
            using var binaryReader = new BinaryReader(stream, _payloadEncoding, true);
            return new QueueState(binaryReader.ReadInt32(), binaryReader.ReadInt64(), binaryReader.ReadInt32(), binaryReader.ReadInt64());
        }
        catch (EndOfStreamException)
        {
            return new QueueState();
        }
    }

    #endregion

    #region DataItem

    public void SerializeDataItem(Stream stream, DataItem item)
    {
        var initPosition = stream.Position;

        try
        {
            SerializeDataItemStartBytes(stream, item.Version);
            SerializeDataLength(stream, item.Data.Length);
            stream.Write(item.Data, 0, item.Data.Length);
            SerializeDataCrc32(stream, Crc32CAlgorithm.Compute(item.Data, 0, item.Data.Length));
        }
        catch (IOException)
        {
            stream.Position = initPosition;
            throw;
        }
    }

    public DataItem DeserializeDataItem(Stream stream)
    {
        var initialPosition = stream.Position;

        try
        {
            var startBytes = DeserializeDataItemStartBytes(stream);
            var dataItemVersion = GetDataItemVersion(startBytes);
            if (dataItemVersion == DataItemVersion.Invalid)
            {
                stream.Position = initialPosition;
                throw new DataRecordException("Invalid header or invalid data item version deserialized.");
            }

            var dataLength = DeserializeDataLength(stream);
            var dataBuffer = ReadBytes(stream, dataLength);
            if (DeserializeDataCrc32(stream) != Crc32CAlgorithm.Compute(dataBuffer))
            {
                stream.Position = initialPosition;
                throw new DataRecordException("Corrupted data record deserialized.");
            }

            return new DataItem(dataItemVersion, dataBuffer);
        }
        catch (EndOfStreamException)
        {
            stream.Position = initialPosition;
            return null;
        }
    }

    public bool TryDeserializeNextValidDataItem(Stream stream, out DataItem dataItem)
    {
        var initialPosition = stream.Position;

        while (StreamLongEnough(stream, DataItemSerializedOverhead))
        {
            var attemptPosition = stream.Position;

            try
            {
                var startBytes = DeserializeDataItemStartBytes(stream);
                var dataItemVersion = GetDataItemVersion(startBytes);
                if (dataItemVersion == DataItemVersion.Invalid)
                {
                    stream.Position = attemptPosition + 1;
                    continue;
                }

                var dataLength = DeserializeDataLength(stream);
                var dataBuffer = ReadBytes(stream, dataLength);
                if (DeserializeDataCrc32(stream) != Crc32CAlgorithm.Compute(dataBuffer))
                {
                    stream.Position = attemptPosition + 1;
                    continue;
                }

                dataItem = new DataItem(dataItemVersion, dataBuffer);
                return true;
            }
            catch (EndOfStreamException)
            {
                break;
            }
        }

        stream.Position = initialPosition;
        dataItem = null;
        return false;
    }

    #endregion

    #endregion

    #region Private Methods

    #region CRC

    /// <summary>
    /// Serializes the given CRC using the given stream at the stream's current position.
    /// </summary>
    private static void SerializeDataCrc32(Stream stream, uint crc)
    {
        using var binaryWriter = new BinaryWriter(stream, _payloadEncoding, true);
        binaryWriter.Write(crc);
    }

    /// <summary>
    /// Deserializes a CRC32 value using the given stream at the stream's current position. If a CRC32 is not found, the stream's position is reset and null is returned.
    /// </summary>
    private static uint DeserializeDataCrc32(Stream stream)
    {
        using var binaryReader = new BinaryReader(stream, _payloadEncoding, true);
        return binaryReader.ReadUInt32();
    }

    #endregion

    #region DataLength

    private static void SerializeDataLength(Stream stream, int dataLength)
    {
        using var binaryWriter = new BinaryWriter(stream, _payloadEncoding, true);
        binaryWriter.Write(dataLength);
    }

    private static int DeserializeDataLength(Stream stream)
    {
        using var binaryReader = new BinaryReader(stream, _payloadEncoding, true);
        return binaryReader.ReadInt32();
    }

    #endregion

    #region Start Record

    private static void SerializeDataItemStartBytes(Stream stream, DataItemVersion dataItemVersion)
    {
        if (dataItemVersion == DataItemVersion.V1)
        {
            stream.Write(_dataItemStartBytesV1, 0, DataItemStartByteCount);
        }
        else if (dataItemVersion == DataItemVersion.V2)
        {
            stream.Write(_dataItemStartBytesV2, 0, DataItemStartByteCount);
        }
        else if (dataItemVersion == DataItemVersion.V3)
        {
            stream.Write(_dataItemStartBytesV3, 0, DataItemStartByteCount);
        }
        else
        {
            throw new ArgumentException("Failed to serialize data item start bytes due to invalid data item version");
        }
    }

    private static byte[] DeserializeDataItemStartBytes(Stream stream)
    {
        return ReadBytes(stream, _dataItemStartBytesV1.Length);
    }

    #endregion

    #region Helpers

    private static byte[] ReadBytes(Stream stream, int count)
    {
        if (!StreamLongEnough(stream, count))
        {
            throw new EndOfStreamException();
        }

        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, buffer.Length);
        while (read != buffer.Length)
        {
            if (read == 0)
            {
                // stream.Read return of 0 means the end of file was reached
                throw new EndOfStreamException();
            }

            read += stream.Read(buffer, 0 + read, buffer.Length - read);
        }

        return buffer;
    }

    private static bool StreamLongEnough(Stream stream, int length)
    {
        return stream.Length - stream.Position >= length;
    }

    private static DataItemVersion GetDataItemVersion(byte[] dataItemStartBytes)
    {
        DataItemVersion dataItemVersion;
        if (_dataItemStartBytesV3.SequenceEqual(dataItemStartBytes))
        {
            dataItemVersion = DataItemVersion.V3;
        }
        else if (_dataItemStartBytesV2.SequenceEqual(dataItemStartBytes))
        {
            dataItemVersion = DataItemVersion.V2;
        }
        else if (_dataItemStartBytesV1.SequenceEqual(dataItemStartBytes))
        {
            dataItemVersion = DataItemVersion.V1;
        }
        else
        {
            dataItemVersion = DataItemVersion.Invalid;
        }

        return dataItemVersion;
    }

    #endregion

    #endregion
}
