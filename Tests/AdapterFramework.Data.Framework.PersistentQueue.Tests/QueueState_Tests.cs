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
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public class QueueState_Tests
{
    [Theory]
    [InlineData(1, 0, 4, 54)]
    [InlineData(1, 0, 1, 54)]
    public void QueueState_IsValid_TrueForValidState(int readerFileNumber, long readerPosition, int writerFileNumber, long writerPosition)
    {
        var queueState = new QueueState(readerFileNumber, readerPosition, writerFileNumber, writerPosition);

        Assert.True(queueState.IsValid());
    }

    [Theory]
    [InlineData(-2, 0, 0, 0)]
    [InlineData(0, -27, 0, 0)]
    [InlineData(0, 0, -21, 0)]
    [InlineData(0, 0, 0, -5)]
    [InlineData(1, 0, 0, -5)]
    [InlineData(5, 0, 4, 54)]
    [InlineData(4, 54, 4, 0)]
    public void QueueState_IsValid_FalseForInvalidState(int readerFileNumber, long readerPosition, int writerFileNumber, long writerPosition)
    {
        var queueState = new QueueState(readerFileNumber, readerPosition, writerFileNumber, writerPosition);

        Assert.False(queueState.IsValid());
    }
}
