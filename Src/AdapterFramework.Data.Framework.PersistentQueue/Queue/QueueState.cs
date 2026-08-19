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
using System.Threading;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

/// <summary>
/// Represents global state for the FileQueue.
/// </summary>
internal class QueueState
{
    private int _readerFileNumber;
    private long _readerPosition;
    private int _writerFileNumber;
    private long _writerPosition;

    public QueueState(int readerFileNumber, long readerPosition, int writerFileNumber, long writerPosition)
    {
        _readerFileNumber = readerFileNumber;
        _readerPosition = readerPosition;
        _writerFileNumber = writerFileNumber;
        _writerPosition = writerPosition;
    }

    public QueueState()
    {
    }

    public int ReaderFileNumber => _readerFileNumber;
    public long ReaderPosition => Interlocked.Read(ref _readerPosition);
    public int WriterFileNumber => _writerFileNumber;
    public long WriterPosition => Interlocked.Read(ref _writerPosition);

    public bool IsValid()
    {
        if (ReaderFileNumber < 0 || ReaderPosition < 0 || WriterFileNumber < 0 || WriterPosition < 0)
        {
            return false;
        }

        if (ReaderFileNumber > WriterFileNumber)
        {
            return false;
        }

        if (ReaderFileNumber == WriterFileNumber && ReaderPosition > WriterPosition)
        {
            return false;
        }

        return true;
    }

    public void SetReaderPosition(long readerPosition) => Interlocked.Exchange(ref _readerPosition, readerPosition);

    public void SetWriterPosition(long writerPosition) => Interlocked.Exchange(ref _writerPosition, writerPosition);

    public void IncrementReaderFileNumber() => Interlocked.Increment(ref _readerFileNumber);

    public void SetReaderFileNumber(int readerFileNumber) => Interlocked.Exchange(ref _readerFileNumber, readerFileNumber);

    public void IncrementWriterFileNumber() => Interlocked.Increment(ref _writerFileNumber);

    public void SetWriterFileNumber(int writerFileNumber) => Interlocked.Exchange(ref _writerFileNumber, writerFileNumber);

    public override string ToString()
    {
        return $"Reader file: {ReaderFileNumber}. Reader position: {ReaderPosition}. Writer file: {WriterFileNumber}. Writer position: {WriterPosition}.";
    }
}
