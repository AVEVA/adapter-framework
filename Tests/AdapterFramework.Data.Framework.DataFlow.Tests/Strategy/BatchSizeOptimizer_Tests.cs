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
using AdapterFramework.Data.Framework.DataFlow.Strategy;
using Xunit;

namespace AdapterFramework.Data.Framework.DataFlow.Tests.Strategy;

public class BatchSizeOptimizer_Tests
{
    private const int MaxByteCount = 192 * 1024;

    private static int _receivedBatchSize;
    private static bool _callbackReceived;

    public BatchSizeOptimizer_Tests()
    {
        _receivedBatchSize = int.MaxValue;
        _callbackReceived = false;
    }

    [Fact]
    public void BatchSizeOptimizer_Normal()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.0,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.9,
            UpperBoundFactor = 1.0,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 100;

        // go over by a factor of 2 at 100 items => should receive callback with < 50
        strategy.Update(itemCount, MaxByteCount * 2, true);

        Assert.True(_receivedBatchSize < 50,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, MaxByteCount / 2, false);

        Assert.True(_receivedBatchSize > itemCount,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");
    }

    [Fact]
    public void BatchSizeOptimizer_Over()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.0,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.9,
            UpperBoundFactor = 1.0,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 100;

        strategy.Update(itemCount, MaxByteCount, true);

        Assert.True(_receivedBatchSize > 90 &&
                      _receivedBatchSize < 100,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, MaxByteCount, false);

        Assert.True(_receivedBatchSize < itemCount,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");
    }

    [Fact]
    public void BatchSizeOptimizer_UpperMid()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.0,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.9,
            UpperBoundFactor = 1.0,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 99;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.99), false);

        Assert.True(_receivedBatchSize > 90 &&
                      _receivedBatchSize < 100,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, MaxByteCount, false);

        Assert.True(_receivedBatchSize < itemCount,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");
    }

    [Fact]
    public void BatchSizeOptimizer_UpperLimit()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.0,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.90,
            UpperBoundFactor = 0.98,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 99;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.99), false);

        Assert.True(_receivedBatchSize > 90 &&
                      _receivedBatchSize < 100,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, MaxByteCount, false);

        Assert.True(_receivedBatchSize < itemCount,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");
    }

    [Fact]
    public void BatchSizeOptimizer_LowerMid()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.0,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.90,
            UpperBoundFactor = 1.00,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 90;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.90), false);

        Assert.True(_receivedBatchSize > 90 &&
                      _receivedBatchSize < 100,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, MaxByteCount, false);

        Assert.True(_receivedBatchSize < itemCount,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");
    }

    [Fact]
    public void BatchSizeOptimizer_OnTarget()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.0,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.90,
            UpperBoundFactor = 1.00,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 95;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.95), false);

        Assert.Equal(itemCount, _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.95), false);

        Assert.Equal(itemCount, _receivedBatchSize);

        Assert.False(_callbackReceived, "Callback was received when it should not have");
    }

    [Fact]
    public void BatchSizeOptimizer_LessThanDelta()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.05,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.90,
            UpperBoundFactor = 1.00,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 95;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.95), false);

        Assert.Equal(itemCount, _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        itemCount = _receivedBatchSize;

        strategy.Update(itemCount, (int)(MaxByteCount * 0.95), false);

        Assert.Equal(itemCount, _receivedBatchSize);

        Assert.False(_callbackReceived, "Callback was received when it should not have");
    }

    [Fact]
    public void BatchSizeOptimizer_NoCallbackUntilChangeInItemCount()
    {
        var parameters = new TuningParameters
        {
            DeltaLimitFactor = 0.00,
            InitialUpperLimit = 100,
            LowerBoundFactor = 0.90,
            UpperBoundFactor = 1.00,
        };

        var strategy = new BatchSizeOptimizer(parameters, MaxByteCount, i =>
        {
            _receivedBatchSize = i;
            _callbackReceived = true;
        });

        var itemCount = 100;

        strategy.Update(itemCount, MaxByteCount, false);

        Assert.True(itemCount > _receivedBatchSize,
            "The actual batch size received was outside the expected range at: " + _receivedBatchSize);

        Assert.True(_callbackReceived, "No callback was received");

        _callbackReceived = false;

        var lastBatchSize = _receivedBatchSize;

        strategy.Update(itemCount, MaxByteCount, false);

        Assert.Equal(lastBatchSize, _receivedBatchSize);

        Assert.False(_callbackReceived, "Callback was received when it should not have");
    }
}
