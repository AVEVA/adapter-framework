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
using System.Collections.Generic;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.MessageProcessor.DataFilters;
using Xunit;
using static AdapterFramework.Data.Framework.MessageProcessor.Tests.DataFilters.Common.DataFilterTestHelper;

namespace AdapterFramework.Data.Framework.MessageProcessor.Tests.DataFilters;

public class AbsoluteDeadbandDataFilter_Tests
{
    private const double FilterValue = 5;
    private readonly DateTime _currentTime;
    private readonly AbsoluteDeadbandDataFilter _defaultFilter = new AbsoluteDeadbandDataFilter(null, FilterValue);

    public AbsoluteDeadbandDataFilter_Tests()
    {
        _currentTime = DateTime.UtcNow;
    }

    [Fact]
    public void CheckDataValue_OutOfOrder()
    {
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 0 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 0 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 0 };
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious);

        Assert.False(sendPrevious);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.True(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out sendPrevious));
        Assert.False(sendPrevious);
    }

    [Fact]
    public void CheckDataValue_NullLastValue()
    {
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 0 };
        var dataSelectionItem = GetSelectionItem<int>();

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        Assert.True(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious));
        Assert.False(sendPrevious);
    }

    [Fact]
    public void CheckDataValue_PastExpirationPeriod()
    {
        var expirationPeriod = new TimeSpan(0, 0, 3);
        var dataFilter = new AbsoluteDeadbandDataFilter(expirationPeriod, FilterValue);
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 13 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 14 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-4), Value = 15 };
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out var sendPrevious);

        Assert.False(sendPrevious);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.True(dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out sendPrevious));
        Assert.False(sendPrevious);
    }

    [Fact]
    public void CheckDataValue_AtExpirationPeriod()
    {
        var expirationPeriod = new TimeSpan(0, 0, 3);
        var dataFilter = new AbsoluteDeadbandDataFilter(expirationPeriod, FilterValue);
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 13 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 14 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-3), Value = 15 };
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out var sendPrevious);

        Assert.False(sendPrevious);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.True(dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out sendPrevious));
        Assert.False(sendPrevious);
    }

    [Fact]
    public void CheckDataValue_LastAndPreviousTimestampEqual()
    {
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 16 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 15 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 10 };
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious);

        Assert.False(sendPrevious);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.True(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out sendPrevious));
        Assert.False(sendPrevious);
    }

    [Fact]
    public void CheckDataValue_BeforeExpirationPeriod()
    {
        var expirationPeriod = new TimeSpan(0, 0, 3);
        var dataFilter = new AbsoluteDeadbandDataFilter(expirationPeriod, FilterValue);
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = 12 };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = 11 };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = 10 };
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out var sendPrevious);

        Assert.False(sendPrevious);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.False(dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out sendPrevious));
        Assert.False(sendPrevious);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckDataValue_Array(bool equal)
    {
        var currentValues = new[] { "4", "5", "6" };
        var previousValues = new[] { "1", "2", "3" };
        var currentValue = new TimeIndexedValue<string[]> { Timestamp = _currentTime, Value = currentValues };
        var previousValue = new TimeIndexedValue<string[]> { Timestamp = _currentTime.AddSeconds(-1), Value = equal ? currentValues : previousValues };
        var lastValue = new TimeIndexedValue<string[]> { Timestamp = _currentTime.AddSeconds(-2), Value = equal ? currentValues : previousValues };
        var dataSelectionItem = GetSelectionItem<string[]>();

        ((DataFilterCachedValues<TimeIndexedValue<string[]>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<string[]>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<string[]>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(!equal, dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious));
        Assert.Equal(!equal, sendPrevious);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckDataValue_Boolean(bool equal)
    {
        var currentValue = new TimeIndexedValue<bool> { Timestamp = _currentTime, Value = true };
        var previousValue = new TimeIndexedValue<bool> { Timestamp = _currentTime.AddSeconds(-1), Value = equal };
        var lastValue = new TimeIndexedValue<bool> { Timestamp = _currentTime.AddSeconds(-2), Value = equal };
        var dataSelectionItem = GetSelectionItem<bool>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<bool>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<bool>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<bool>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(!equal, dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious));
        Assert.Equal(!equal, sendPrevious);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DataFilteringChecks_Int16(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<short>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<short>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<short>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<short>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_Int16(short current, short previous, short last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<short> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<short> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<short> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<short>();

        ((DataFilterCachedValues<TimeIndexedValue<short>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<short>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<short>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DataFilteringChecks_Int32(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<int>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<int>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<int>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_Int32(int current, int previous, int last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<int> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<int> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<int>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<int>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DataFilteringChecks_Int64(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<long>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<long>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<long>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<long>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_Int64(long current, long previous, long last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<long> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<long> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<long> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<long>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<long>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<long>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<long>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void DataFilteringChecks_UInt16(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<ushort>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<ushort>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<ushort>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<ushort>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_UInt16(ushort current, ushort previous, ushort last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<ushort> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<ushort> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<ushort> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<ushort>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<ushort>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<ushort>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<ushort>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void DataFilteringChecks_UInt32(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<uint>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<uint>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<uint>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<uint>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_Uint32(uint current, uint previous, uint last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<uint> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<uint> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<uint> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<uint>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<uint>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<uint>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<uint>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void DataFilteringChecks_UInt64(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<ulong>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<ulong>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<ulong>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<ulong>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_UInt64(ulong current, ulong previous, ulong last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<ulong> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<ulong> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<ulong> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<ulong>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<ulong>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<ulong>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<ulong>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DataFilteringChecks_Float32(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<float>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<float>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<float>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<float>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_Float32(float current, float previous, float last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<float> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<float> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<float> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<float>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<float>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<float>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<float>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DataFilteringChecks_Double(bool sameTimestamps, bool negativeValues)
    {
        var receivedValues = new List<TimeIndexedValue<double>>();
        var valuesToSend = GetAbsoluteTimeIndexedValuesToSend<double>(_currentTime, sameTimestamps, negativeValues);
        var expectedValues = GetAbsoluteExpectedTimeIndexedValues<double>(_currentTime, sameTimestamps, negativeValues);
        var dataSelectionItem = GetSelectionItem<double>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(16, 15, 10, true)]
    [InlineData(4, 5, 10, true)]
    [InlineData(15, 14, 10, false)]
    [InlineData(14, 13, 10, false)]
    [InlineData(6, 7, 10, false)]
    public void CheckDataFilter_Double(double current, double previous, double last, bool sendData)
    {
        var currentValue = new TimeIndexedValue<double> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<double> { Timestamp = _currentTime.AddSeconds(-1), Value = previous };
        var lastValue = new TimeIndexedValue<double> { Timestamp = _currentTime.AddSeconds(-2), Value = last };
        var dataSelectionItem = GetSelectionItem<double>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<double>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);

        ((DataFilterCachedValues<TimeIndexedValue<double>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<double>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious), sendData);
        Assert.Equal(sendPrevious, sendData);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckDataValue_Dictionaries(bool equal)
    {
        var current = new Dictionary<string, string> { { "current", "value" } };
        var previous = new Dictionary<string, string> { { "previous", "value" } };
        var currentValue = new TimeIndexedValue<Dictionary<string, string>> { Timestamp = _currentTime, Value = current };
        var previousValue = new TimeIndexedValue<Dictionary<string, string>> { Timestamp = _currentTime.AddSeconds(-1), Value = equal ? current : previous };
        var lastValue = new TimeIndexedValue<Dictionary<string, string>> { Timestamp = _currentTime.AddSeconds(-2), Value = equal ? current : previous };
        var dataSelectionItem = GetSelectionItem<Dictionary<string, string>>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<Dictionary<string, string>>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);
        ((DataFilterCachedValues<TimeIndexedValue<Dictionary<string, string>>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<Dictionary<string, string>>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(!equal, dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious));
        Assert.Equal(!equal, sendPrevious);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckDataValue_Strings(bool equal)
    {
        const string CurrentValueString = "currentValue";
        const string PreviousValueString = "previousValue";
        var currentValue = new TimeIndexedValue<string> { Timestamp = _currentTime, Value = CurrentValueString };
        var previousValue = new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-1), Value = equal ? CurrentValueString : PreviousValueString };
        var lastValue = new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-2), Value = equal ? CurrentValueString : PreviousValueString };
        var dataSelectionItem = GetSelectionItem<string>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<string>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);
        ((DataFilterCachedValues<TimeIndexedValue<string>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<string>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(!equal, dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious));
        Assert.Equal(!equal, sendPrevious);
    }

    [Fact]
    public void CheckDataValue_NullStrings()
    {
        var receivedValues = new List<TimeIndexedValue<string>>();
        var valuesToSend = new List<TimeIndexedValue<string>>
        {
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-10), Value = "test" },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-9), Value = null },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-8), Value = "test" },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-7), Value = null },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-6), Value = "test" },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-5), Value = null },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-4), Value = "test" },
            new TimeIndexedValue<string> { Timestamp = _currentTime.AddSeconds(-3), Value = null },
        };

        var expectedValues = new List<TimeIndexedValue<string>>(valuesToSend);
        var dataSelectionItem = GetSelectionItem<string>(AbsoluteDataFilterId);
        var messageProcessor = GetMessageProcessor(receivedValues);

        foreach (var value in valuesToSend)
        {
            messageProcessor.WriteDynamicValue(dataSelectionItem, value, MessageAction.Default);
        }

        ValidateValues(expectedValues, receivedValues);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CheckDataValue_DateTime(bool equal)
    {
        var currentValue = new TimeIndexedValue<DateTime> { Timestamp = _currentTime, Value = _currentTime };
        var previousValue = new TimeIndexedValue<DateTime> { Timestamp = _currentTime.AddSeconds(-1), Value = equal ? _currentTime : _currentTime.AddSeconds(-1) };
        var lastValue = new TimeIndexedValue<DateTime> { Timestamp = _currentTime.AddSeconds(-2), Value = equal ? _currentTime : _currentTime.AddSeconds(-2) };
        var dataSelectionItem = GetSelectionItem<DateTime>(AbsoluteDataFilterId);

        ((DataFilterCachedValues<TimeIndexedValue<DateTime>>)dataSelectionItem.DataFilterCache).SetCurrentValue(lastValue);
        dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out _);
        ((DataFilterCachedValues<TimeIndexedValue<DateTime>>)dataSelectionItem.DataFilterCache).SetCurrentValue(currentValue);
        ((DataFilterCachedValues<TimeIndexedValue<DateTime>>)dataSelectionItem.DataFilterCache).SetPreviousValue(previousValue);

        Assert.Equal(!equal, dataSelectionItem.DataFilterCache.CheckDataFilter(_defaultFilter, out var sendPrevious));
        Assert.Equal(!equal, sendPrevious);
    }
}
