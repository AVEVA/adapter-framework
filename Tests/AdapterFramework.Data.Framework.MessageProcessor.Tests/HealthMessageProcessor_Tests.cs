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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using Xunit;

namespace AdapterFramework.Data.Framework.MessageProcessor.Tests;

public class HealthMessageProcessor_Tests
{
    private const int WaitTime = 1500;
    private const string TestComponentId = "TestComponentId";
    private const string TestComponentType = "TestComponenType";
    private const string TestStreamIdBase = "TestStream";

    [Fact]
    public void HealthMessageProcessor_WriteHealthTypes_InvalidInput()
    {
        DataType[] invalidTypes = null;

        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        using var messageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object);

        Assert.Throws<ArgumentNullException>(() => messageProcessor.WriteHealthTypes(invalidTypes, MessageAction.Default));
    }

    [Fact]
    public void HealthMessageProcessor_WriteHealthTypes()
    {
        var expectedTypeMessage = new DynamicDataType
        {
            Id = "TestType",
            Description = "TestDescription",
            Properties = new Dictionary<string, PropertyDefinition>()
            {
                { "Value", new PropertyDefinition { Type = "string", Format = "string" } },
            },
        };

        DataType[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        mockEndpointManager.Setup(em => em.SendMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize<DataType[]>(It.IsAny<DataType[]>())).Returns(Array.Empty<byte>())
            .Callback<DataType[]>(type =>
            {
                receivedMessage = type;
            });

        using var messageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object);

        messageProcessor.WriteHealthTypes(new DataType[] { expectedTypeMessage }, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedTypeMessage, receivedMessage[0]);
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void HealthMessageProcessor_WriteHealthStreams_InvalidInput()
    {
        DataStream[] invalidContainers = null;

        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        using var messageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object);

        Assert.Throws<ArgumentNullException>(() => messageProcessor.WriteHealthStreams(invalidContainers, MessageAction.Default));
    }

    [Fact]
    public void HealthMessageProcessor_WriteHealthStreams()
    {
        var expectedContainerMessage = new DataStream { Id = "TestContainer", Name = "Container", TypeId = "TestType" };
        DataStream[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        mockEndpointManager.Setup(em => em.SendMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize<DataStream[]>(It.IsAny<DataStream[]>())).Returns(Array.Empty<byte>())
            .Callback<DataStream[]>(container =>
            {
                receivedMessage = container;
            });

        using var messageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object);

        messageProcessor.WriteHealthStreams(new DataStream[] { expectedContainerMessage }, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedContainerMessage, receivedMessage[0]);
        Assert.NotNull(sentMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void HealthMessageProcessor_WriteHealthData_InvalidInput(string containerId)
    {
        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        using var messageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object);

        var argumentExceptionThrown = false;

        try
        {
            messageProcessor.WriteHealthValue(containerId, Classification.Dynamic, 42, MessageAction.Default);
        }
        catch (ArgumentException)
        {
            argumentExceptionThrown = true;
        }

        Assert.True(argumentExceptionThrown);
    }

    [Fact]
    public void HealthMessageProcessor_WriteHealthValue()
    {
        var expectedContainerId = "TestContainerId";
        var expectedDataInstance = 42;

        StreamData[] receivedMessage = null;

        ISerializedOmfMessage sentMessage = null;

        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogger = new Mock<ILogger>();

        mockEndpointManager.Setup(em => em.SendMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize<StreamData[]>(It.IsAny<StreamData[]>())).Returns(Array.Empty<byte>())
            .Callback<StreamData[]>(omfData =>
            {
                receivedMessage = omfData;
            });

        using var messageProcessor = new HealthMessageProcessor(mockLogger.Object, mockSerializer.Object, null, mockEndpointManager.Object);

        messageProcessor.WriteHealthValue(expectedContainerId, Classification.Dynamic, expectedDataInstance, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedContainerId, receivedMessage[0].Id);
        Assert.Equal(expectedDataInstance, (int)receivedMessage[0].Values.ToList().FirstOrDefault());
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public async Task HealthMessageProcessor_WriteStreams_Metadata_Test()
    {
        const int ItemCount = 2;
        var receivedMessages = new List<DataStream>();
        var streamMessages = new DataStream[ItemCount];
        var expectedMessages = new DataStream[ItemCount];
        var timeout = TimeSpan.FromSeconds(5);
        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        var tcs = new TaskCompletionSource<bool>();

        mockEndpointManager.Setup(em => em.SendMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
            });

        mockSerializer.Setup(serializer => serializer.Serialize<DataStream[]>(It.IsAny<DataStream[]>())).Returns(Array.Empty<byte>())
            .Callback<DataStream[]>(container =>
            {
                receivedMessages.AddRange(container);
                tcs.SetResult(true);
            });

        using var healthMessageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object)
        {
            StreamMetadataLevel = MetadataInfo.Low,
        };

        var metaDataDict = new Dictionary<string, object>
        {
            { EdgeSystemConstants.AdapterTypeString, TestComponentType },
            { EdgeSystemConstants.DataSourceString, TestComponentId },
        };

        var existingMetaDataDict = new Dictionary<string, object>
        {
            { EdgeSystemConstants.AdapterTypeString, TestComponentType },
            { EdgeSystemConstants.DataSourceString, TestComponentId },
        };

        for (int i = 0; i < ItemCount; i++)
        {
            streamMessages[i] = new DataStream { Id = TestStreamIdBase + i, Metadata = existingMetaDataDict };
            expectedMessages[i] = new DataStream { Id = TestStreamIdBase + i, Metadata = metaDataDict };
        }

        healthMessageProcessor.WriteHealthStreams(streamMessages, MessageAction.Default);
        using var cts = new CancellationTokenSource(timeout);
        cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

        try
        {
            await tcs.Task;
        }
        catch (TaskCanceledException)
        {
            Assert.Fail($"Failed to write streams in the given amount of time (${timeout}.");
        }

        Assert.Equal(ItemCount, receivedMessages.Count);

        for (int j = 0; j < ItemCount; j++)
        {
            Assert.Equal(expectedMessages[j].Metadata.Count, receivedMessages[j].Metadata.Count);
            Assert.Equal(expectedMessages[j].Metadata[EdgeSystemConstants.AdapterTypeString], receivedMessages[j].Metadata[EdgeSystemConstants.AdapterTypeString]);
            Assert.Equal(expectedMessages[j].Metadata[EdgeSystemConstants.DataSourceString], receivedMessages[j].Metadata[EdgeSystemConstants.DataSourceString]);
        }
    }

    [Fact]
    public void HealthMessageProcessor_WriteStreams_Metadata_Should_Get_Removed_Test()
    {
        const int ItemCount = 2;
        var receivedMessages = new List<DataStream>();
        var streamMessages = new DataStream[ItemCount];
        var mockEndpointManager = new Mock<IOmfHealthEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();

        mockSerializer.Setup(serializer => serializer.Serialize<DataStream[]>(It.IsAny<DataStream[]>())).Returns(Array.Empty<byte>())
            .Callback<DataStream[]>(container =>
            {
                receivedMessages.AddRange(container);
            });

        using var healthMessageProcessor = new HealthMessageProcessor(null, mockSerializer.Object, null, mockEndpointManager.Object)
        {
            StreamMetadataLevel = MetadataInfo.None,
        };

        var existingMetaDataDict = new Dictionary<string, object>
        {
            { EdgeSystemConstants.AdapterTypeString, TestComponentType },
            { EdgeSystemConstants.DataSourceString, TestComponentId },
        };

        for (int i = 0; i < ItemCount; i++)
        {
            streamMessages[i] = new DataStream { Id = TestStreamIdBase + i, Metadata = existingMetaDataDict };
        }

        healthMessageProcessor.WriteHealthStreams(streamMessages, MessageAction.Default);

        Thread.Sleep(WaitTime);

        Assert.Equal(ItemCount, receivedMessages.Count);

        for (int j = 0; j < ItemCount; j++)
        {
            Assert.Null(receivedMessages[j].Metadata);
        }
    }
}
