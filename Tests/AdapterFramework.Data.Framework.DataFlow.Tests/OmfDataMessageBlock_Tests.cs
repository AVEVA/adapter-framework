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
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

public class OmfDataMessageBlock_Tests
{
    private const string TestComponentId = "OpcUa1";
    private const int WaitTime = 5_000;
    private const int Capacity = 500;
    private const int Count = 15;
    private const int NumberOfItems = 42;
    private const int ExtendedStaticMessagesCount = 10;
    private readonly List<OmfMessage<StreamData>> _sentMessage;
    private int _returnedCount;
    private MessageAction _flushMessageAction;

    public OmfDataMessageBlock_Tests()
    {
        _returnedCount = 0;
        _sentMessage = new List<OmfMessage<StreamData>>();
    }

    [Fact]
    public void OmfDataMessageBlock_Post_VerifyOneMessageCorrectCount_Test()
    {
        var mockLogger = new Mock<ILogger>();
        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        for (var index = 0; index < NumberOfItems; index++)
        {
            var groupedValues = new Dictionary<string, (Classification, List<object>)>
            {
                { index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(Classification.Dynamic, Count) },
            };

            omfDataMessageBlock.Post(new GroupedDataMessage(Count, groupedValues, null, null, MessageAction.Update));
        }

        SpinWait.SpinUntil(() => _sentMessage.Count == NumberOfItems, WaitTime);

        Assert.True(_returnedCount == Count * NumberOfItems, $"Passed message Count {Count * NumberOfItems} does not match output Count {_returnedCount}");
        Assert.Equal(MessageAction.Update, _flushMessageAction);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void OmfDataMessageBlock_Post_VerifyOneMessageCorrectCount_TestWithExtendedPropertyMessage(int extendedStaticMessagesCount)
    {
        var mockLogger = new Mock<ILogger>();
        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        for (var index = 0; index < NumberOfItems; index++)
        {
            var groupedValues = new Dictionary<string, (Classification, List<object>)>
            {
                { index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(Classification.Dynamic, Count) },
            };

            var extendedStaticMessages = GetExtendedStaticMessages(extendedStaticMessagesCount, false);
            omfDataMessageBlock.Post(new GroupedDataMessage(Count, groupedValues, extendedStaticMessages, null, MessageAction.Update));
        }

        Assert.True(SpinWait.SpinUntil(() => _returnedCount == (Count + extendedStaticMessagesCount) * NumberOfItems, WaitTime),
            $"Passed message Count {(Count + extendedStaticMessagesCount) * NumberOfItems} does not match output Count {_returnedCount}");

        Assert.Equal(MessageAction.Update, _flushMessageAction);
    }

    [Fact]
    public void OmfDataMessageBlock_Post_VerifyMultipleMessagesCorrectCount_Test()
    {
        var mockLogger = new Mock<ILogger>();

        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        for (var index = 0; index < NumberOfItems; index++)
        {
            var groupedValues = new Dictionary<string, (Classification, List<object>)>
            {
                { index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(Classification.Dynamic, Count) },
            };

            var extendedPropertyMessages = GetExtendedStaticMessages(ExtendedStaticMessagesCount, true);
            omfDataMessageBlock.Post(new GroupedDataMessage(Count, groupedValues, extendedPropertyMessages, null,MessageAction.Update));
        }

        Assert.True(SpinWait.SpinUntil(() => _returnedCount == (Count + ExtendedStaticMessagesCount) * NumberOfItems, WaitTime),
            $"Passed message Count {(Count + ExtendedStaticMessagesCount) * NumberOfItems} does not match output Count {_returnedCount}");

        Assert.Equal(MessageAction.Update, _flushMessageAction);
    }

    [Fact]
    public void OmfDataMessageBlock_Post_VerifyLinksLast_Test()
    {
        var mockLogger = new Mock<ILogger>();

        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        var groupedValues = new Dictionary<string, (Classification, List<object>)>
        {
            { Tokens.Link, (Classification.Static, new List<object> { "TestLink" }) },
        };

        for (var index = 0; index < NumberOfItems; index++)
        {
            groupedValues.Add(index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(Classification.Dynamic, Count));
        }

        omfDataMessageBlock.Post(new GroupedDataMessage(Count * NumberOfItems, groupedValues, null, null, MessageAction.Update));

        Assert.True(SpinWait.SpinUntil(() => _sentMessage.Count == 2, WaitTime));
        Assert.NotNull(_sentMessage);
        Assert.Equal(Tokens.Link, _sentMessage[1].Values.Last().Id);
        Assert.Equal(MessageAction.Update, _flushMessageAction);
    }

    [Fact]
    public void OmfDataMessageBlock_Post_StaticData_WithDataSource_Test()
    {
        var mockLogger = new Mock<ILogger>();

        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        var groupedValues = new Dictionary<string, (Classification Classification, List<object> GroupedValues)>
        {
            { "Static1", GetGroupedValues(Classification.Static, Count) },
        };

        var extendedStaticMessagesCount = new Random().Next(1, 10);
        var expectedValuesCount = Count + extendedStaticMessagesCount;
        omfDataMessageBlock.Post(new GroupedDataMessage(expectedValuesCount, groupedValues, GetExtendedStaticMessages(extendedStaticMessagesCount), null, MessageAction.Update));

        SpinWait.SpinUntil(() => _sentMessage.Count == expectedValuesCount, WaitTime);

        Assert.NotNull(_sentMessage);
        Assert.Equal(expectedValuesCount, _returnedCount);
        Assert.Equal(MessageAction.Update, _flushMessageAction);

        Assert.True(_sentMessage[0].Values is StaticStreamData[]);
        Assert.All((StreamData[])_sentMessage[0].Values[1..], val =>
        {
            Assert.True(val is StaticStreamData);
            Assert.Equal(TestComponentId, ((StaticStreamData)val).DataSource);
        });

        // Assert.All(_sentMessage[0].Values, value => Assert.True(value.Values));
        Assert.Equal(MessageAction.Update, _flushMessageAction);
    }

    [Theory]
    [InlineData(Classification.Dynamic, Classification.Static, Classification.Static, 2)]
    [InlineData(Classification.Dynamic, Classification.Dynamic, Classification.Static, 2)]
    [InlineData(Classification.Dynamic, Classification.Dynamic, Classification.Dynamic, 2)]
    [InlineData(Classification.Static, Classification.Static, Classification.Static, 1)]
    public void OmfDataMessageBlock_Post_MessageSeparationByClassification_Test(Classification classification1, Classification classification2, Classification classification3, int expectedMessageCount)
    {
        var mockLogger = new Mock<ILogger>();

        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        var groupedValues = new Dictionary<string, (Classification, List<object>)>();

        for (var index = 0; index < NumberOfItems; index++)
        {
            groupedValues.Add(nameof(classification1) + index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(classification1, Count));
        }

        for (var index = 0; index < NumberOfItems; index++)
        {
            groupedValues.Add(nameof(classification2) + index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(classification2, Count));
        }

        for (var index = 0; index < NumberOfItems; index++)
        {
            groupedValues.Add(nameof(classification3) + index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(classification3, Count));
        }

        var extendedStaticMessageCount = new Random().Next(1, 10);
        var extendedStaticMessages = GetExtendedStaticMessages(extendedStaticMessageCount);

        var expectedValuesCount = (Count * NumberOfItems * 3) + extendedStaticMessageCount;
        omfDataMessageBlock.Post(new GroupedDataMessage(expectedValuesCount, groupedValues, extendedStaticMessages, null, MessageAction.Update));

        SpinWait.SpinUntil(() => _sentMessage.Count == expectedMessageCount, WaitTime);
        Assert.NotNull(_sentMessage);

        switch (classification1)
        {
            case Classification.Dynamic:
                {
                    Assert.True(_sentMessage[0].Values is DynamicStreamData[]);
                    if (expectedMessageCount == 2)
                    {
                        Assert.True(_sentMessage[1].Values is StaticStreamData[]);
                    }
                }

                break;
            case Classification.Static:
                Assert.True(_sentMessage[0].Values is StaticStreamData[]);
                break;
        }

        Assert.True(_sentMessage[^1].Values is StaticStreamData[]);
        Assert.Equal(expectedValuesCount, _returnedCount);
        Assert.Equal(MessageAction.Update, _flushMessageAction);
    }

    [Theory]
    [InlineData(MessageAction.Create)]
    [InlineData(MessageAction.Update)]
    [InlineData(MessageAction.Delete)]
    [InlineData(MessageAction.Default)]
    public void OmfDataMessageBlock_Post_DifferentMessageActions_Test(MessageAction messageAction)
    {
        var mockLogger = new Mock<ILogger>();

        using var omfDataMessageBlock = new OmfDataMessageBlock(mockLogger.Object, Capacity, FlushAction, CancellationToken.None);

        for (var index = 0; index < NumberOfItems; index++)
        {
            var groupedValues = new Dictionary<string, (Classification, List<object>)>
            {
                { index.ToString(CultureInfo.InvariantCulture), GetGroupedValues(Classification.Dynamic, Count) },
            };

            var extendedPropertyMessages = GetExtendedStaticMessages(ExtendedStaticMessagesCount, true);
            omfDataMessageBlock.Post(new GroupedDataMessage(Count, groupedValues, extendedPropertyMessages, null, messageAction));
        }

        Assert.True(SpinWait.SpinUntil(() => _returnedCount == (Count + ExtendedStaticMessagesCount) * NumberOfItems, WaitTime),
            $"Passed message Count {(Count + ExtendedStaticMessagesCount) * NumberOfItems} does not match output Count {_returnedCount}");

        Assert.Equal(messageAction, _flushMessageAction);
    }

    private static (Classification Classification, List<object> GroupedValues) GetGroupedValues(Classification classification, int valueCount)
    {
        var streamValues = new List<object>();
        for (var i = 0; i < valueCount; i++)
        {
            streamValues.Add(i);
        }

        return (classification, streamValues);
    }

    private static List<StaticDataMessage> GetExtendedStaticMessages(int count, bool uniqueId = false)
    {
        var id = "ExtId";
        var extendedPropertyMessages = new List<StaticDataMessage>(count);

        for (int i = 0; i < count; i++)
        {
            dynamic instance1 = new ExpandoObject();
            instance1.Serial = "FN-2187";
            instance1.Description = "Main Tank";
            instance1.ModelVersion = i * i;

            if (uniqueId == true)
            {
                id += i;
            }

            var extendedProperties = new Dictionary<string, PropertyDefinition> { { "ModelVersion", new PropertyDefinition { Name = $"Model{i}" } } };

            var staticDataMessage = new StaticDataMessage("TypeId", $"Tank{i}", $"Tank {i}", $"Description {i}", TestComponentId, null, extendedProperties, null, null,
                instance1, MessageAction.Default);

            extendedPropertyMessages.Add(staticDataMessage);
        }

        return extendedPropertyMessages;
    }

    private void FlushAction(Message message)
    {
        void ProcessData(int count, StreamData[] streamData, MessageAction messageAction)
        {
            var totalCount = streamData.Sum(valueObject => valueObject.Values.Count());

            _returnedCount += totalCount;
            _flushMessageAction = messageAction;

            _sentMessage.Add(new OmfMessage<StreamData>(count, streamData, messageAction));
        }

        switch (message)
        {
            case OmfMessage<DynamicStreamData> dynamicData:
                {
                    if (dynamicData.Values is StreamData[] dynamicStreamData)
                    {
                        ProcessData(dynamicData.Count, dynamicStreamData, dynamicData.MessageAction);
                    }

                    break;
                }

            case OmfMessage<StaticStreamData> staticData:
                {
                    if (staticData.Values is StreamData[] staticStreamData)
                    {
                        ProcessData(staticData.Count, staticStreamData, staticData.MessageAction);
                    }

                    break;
                }
        }
    }
}
