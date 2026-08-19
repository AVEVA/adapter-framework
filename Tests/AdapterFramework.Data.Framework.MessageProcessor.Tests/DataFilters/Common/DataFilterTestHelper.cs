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
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.MessageProcessor.DataFilters;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.MessageProcessor.Tests.DataFilters.Common;

public static class DataFilterTestHelper
{
    public const string AbsoluteDataFilterId = "AbsoluteFilterId";
    public const string PercentChangeDataFilterId = "PercentFilterId";
    public const string DataSelectionId = "TestId";

    public static TestDataSelectionItem GetSelectionItem<T>(string dataFilterId = null) => new(DataSelectionId, dataFilterId)
    {
        DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<T>>(),
    };

    public static void ValidateValues<T>(IList<TimeIndexedValue<T>> expectedValues, IList<TimeIndexedValue<T>> receivedValues)
    {
        Assert.Equal(expectedValues.Count, receivedValues.Count);

        for (var i = 0; i < expectedValues.Count; i++)
        {
            Assert.Equal(expectedValues[i].Value, receivedValues[i].Value);
        }
    }

    public static AdapterMessageProcessor GetMessageProcessor<T>(ICollection<TimeIndexedValue<T>> receivedValues)
    {
        var dataFiltersConfiguration = GetDataFiltersConfiguration();

        var mockMessageProcessor = new Mock<IMessageProcessor>();
        mockMessageProcessor.Setup(processor => processor.WriteValue(It.IsAny<string>(), It.IsAny<Classification>(), It.IsAny<TimeIndexedValue<T>>(), It.IsAny<MessageAction>()))
            .Callback((string id, Classification classification, TimeIndexedValue<T> value, MessageAction messageAction) => receivedValues.Add(value));

        mockMessageProcessor.Setup(processor => processor.WriteDynamicValue(It.IsAny<string>(), It.IsAny<TimeIndexedValue<T>>(), It.IsAny<MessageAction>(), It.IsAny<PartitionKey?>()))
            .Callback((string id, TimeIndexedValue<T> value, MessageAction messageAction, PartitionKey? partitionKey) => receivedValues.Add(value));

        var messageProcessor = new AdapterMessageProcessor(mockMessageProcessor.Object, OmfVersion.Omf12);

        messageProcessor.ProcessDataFiltersConfigurationChanges(dataFiltersConfiguration);

        return messageProcessor;
    }

    public static IList<TimeIndexedValue<T>> GetAbsoluteTimeIndexedValuesToSend<T>(DateTime currentTime, bool sameTimestamp = false, bool negativeValues = false) where T : struct
    {
        if (sameTimestamp)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)101 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)102 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)103 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)104 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)103 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)105 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)106 },
            };
        }

        if (negativeValues)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)(-100) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-9), Value = (T)(dynamic)(-101) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-8), Value = (T)(dynamic)(-102) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-7), Value = (T)(dynamic)(-103) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-6), Value = (T)(dynamic)(-104) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-5), Value = (T)(dynamic)(-103) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)(-105) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)(-106) },
            };
        }

        return new List<TimeIndexedValue<T>>
        {
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-9), Value = (T)(dynamic)101 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-8), Value = (T)(dynamic)102 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-7), Value = (T)(dynamic)103 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-6), Value = (T)(dynamic)104 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-5), Value = (T)(dynamic)103 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)105 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)106 },
        };
    }

    public static IList<TimeIndexedValue<T>> GetAbsoluteExpectedTimeIndexedValues<T>(DateTime currentTime, bool sameTimestamp = false, bool negativeValues = false) where T : struct
    {
        if (sameTimestamp)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)106 },
            };
        }

        if (negativeValues)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)(-100) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)(-105) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)(-106) },
            };
        }

        return new List<TimeIndexedValue<T>>
        {
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)105 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)106 },
        };
    }

    public static IList<TimeIndexedValue<T>> GetPercentTimeIndexedValuesToSend<T>(DateTime currentTime, bool sameTimestamp = false, bool negativeValues = false) where T : struct
    {
        if (sameTimestamp)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)125 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)126 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)130 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)135 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)140 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)145 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)151 },
            };
        }

        if (negativeValues)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)(-100) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-9), Value = (T)(dynamic)(-125) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-8), Value = (T)(dynamic)(-126) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-7), Value = (T)(dynamic)(-130) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-6), Value = (T)(dynamic)(-135) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-5), Value = (T)(dynamic)(-140) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)(-145) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)(-151) },
            };
        }

        return new List<TimeIndexedValue<T>>
        {
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-9), Value = (T)(dynamic)125 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-8), Value = (T)(dynamic)126 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-7), Value = (T)(dynamic)130 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-6), Value = (T)(dynamic)135 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-5), Value = (T)(dynamic)140 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)145 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)151 },
        };
    }

    public static IList<TimeIndexedValue<T>> GetPercentExpectedTimeIndexedValues<T>(DateTime currentTime, bool sameTimestamp = false, bool negativeValues = false) where T : struct
    {
        if (sameTimestamp)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)151 },
            };
        }

        if (negativeValues)
        {
            return new List<TimeIndexedValue<T>>
            {
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)(-100) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)(-145) },
                new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)(-151) },
            };
        }

        return new List<TimeIndexedValue<T>>
        {
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-10), Value = (T)(dynamic)100 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-4), Value = (T)(dynamic)145 },
            new TimeIndexedValue<T> { Timestamp = currentTime.AddSeconds(-3), Value = (T)(dynamic)151 },
        };
    }

    private static DataFiltersConfiguration[] GetDataFiltersConfiguration(double deadband = 5D, double? percentChange = null, TimeSpan? expirationPeriod = null)
    {
        var configuration = new DataFiltersConfiguration[2];
        configuration[0] = new DataFiltersConfiguration
        {
            Id = PercentChangeDataFilterId,
            AbsoluteDeadband = null,
            PercentChange = 50,
            ExpirationPeriod = expirationPeriod,
        };

        configuration[1] = new DataFiltersConfiguration
        {
            Id = AbsoluteDataFilterId,
            AbsoluteDeadband = deadband,
            PercentChange = percentChange,
            ExpirationPeriod = expirationPeriod,
        };

        return configuration;
    }
}
