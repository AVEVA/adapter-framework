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
using System.Collections.Generic;
using Xunit;

namespace AdapterFramework.Data.Framework.Extensions.Tests;

public class QueueExtensions_Tests
{
    [Fact]
    public void QueueExtensions_TryDeque_EmptyQueue_Test()
    {
        var testQueue = new Queue<int>();
        Assert.False(testQueue.TryDequeue(out _));
    }

    [Fact]
    public void QueueExtensions_TryPeek_EmptyQueue_Test()
    {
        var testQueue = new Queue<int>();
        Assert.False(testQueue.TryPeek(out _));
    }

    [Fact]
    public void QueueExtensions_TryPeek_Test()
    {
        var testQueue = new Queue<int>();
        var valueToEnqueue = 42;

        testQueue.Enqueue(valueToEnqueue);

        Assert.True(testQueue.TryPeek(out var result));
        Assert.Equal(valueToEnqueue, result);
        Assert.True(testQueue.TryPeek(out var result2));
        Assert.Equal(valueToEnqueue, result2);
    }

    [Fact]
    public void QueueExtensions_TryDequeue_Test()
    {
        var testQueue = new Queue<int>();
        var valueToEnqueue = 42;

        testQueue.Enqueue(valueToEnqueue);

        Assert.True(testQueue.TryDequeue(out var result));
        Assert.Equal(valueToEnqueue, result);
        Assert.False(testQueue.TryDequeue(out _));
    }
}
