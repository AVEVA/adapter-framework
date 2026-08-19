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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using Moq;
using AdapterFramework.Data.Framework.Serialization;
using Xunit;
using DataType = AdapterFramework.Data.DataModel.DataType;

namespace AdapterFramework.Data.Framework.EgressComponent.Tests;

public class DataMessageProcessor_Tests
{
    private const int WaitTime = 15000;
    private readonly object sentMessagesLock = new object();

    [Fact]
    public void DataMessageProcessor_WriteType_InvalidInput()
    {
        DataType invalidType = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        Assert.Throws<ArgumentNullException>(() => messageProcessor.WriteType(invalidType, MessageAction.Default));
    }

    [Fact]
    public void DataMessageProcessor_WriteTypes_InvalidInput()
    {
        DataType[] invalidTypes = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        Assert.Throws<ArgumentNullException>(() => messageProcessor.WriteTypes(invalidTypes, MessageAction.Default));
    }

    [Fact]
    public void DataMessageProcessor_WriteTypes()
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

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize<DataType[]>(It.IsAny<DataType[]>())).Returns(Array.Empty<byte>())
            .Callback<DataType[]>(type =>
            {
                receivedMessage = type;
            });

        mockEgressComponentIdService.Setup(idService => idService.ComponentId).Returns("SampleId");

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, 
            mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteTypes(new DataType[] { expectedTypeMessage }, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedTypeMessage, receivedMessage[0]);
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_WriteType()
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

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<DataType[]>())).Returns(Array.Empty<byte>())
            .Callback<DataType[]>(type =>
            {
                receivedMessage = type;
            });

        mockEgressComponentIdService.Setup(idService => idService.ComponentId).Returns("SampleId");

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteType(expectedTypeMessage, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedTypeMessage, receivedMessage[0]);
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_WriteStream_InvalidInput()
    {
        DataStream invalidStream = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        Assert.Throws<ArgumentNullException>(() => messageProcessor.WriteStream(invalidStream, MessageAction.Default));
    }

    [Fact]
    public void DataMessageProcessor_WriteStreams_InvalidInput()
    {
        DataStream[] invalidStreams = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        Assert.Throws<ArgumentNullException>(() => messageProcessor.WriteStreams(invalidStreams, MessageAction.Default));
    }

    [Fact]
    public void DataMessageProcessor_WriteStream()
    {
        using var mre = new ManualResetEventSlim(false);

        var expectedStreamMessage = new DataStream { Id = "TestStream", Name = "Stream", TypeId = "TestType" };
        DataStream[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
                mre.Set();
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<DataStream[]>())).Returns(Array.Empty<byte>())
            .Callback<DataStream[]>(stream =>
            {
                receivedMessage = stream;
            });

        mockEgressComponentIdService.Setup(idService => idService.ComponentId).Returns("SampleId");

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteStream(expectedStreamMessage, MessageAction.Default);

        // give it some time to TPL to process the message
        if (!mre.Wait(WaitTime * 10))
        {
            Assert.Fail("Did not recieve message.");
        }

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedStreamMessage, receivedMessage[0]);
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_WriteStreams()
    {
        using var mre = new ManualResetEventSlim(false);

        var expectedStreamMessage = new DataStream { Id = "TestStream", Name = "Stream", TypeId = "TestType" };
        DataStream[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
                mre.Set();
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<DataStream[]>())).Returns(Array.Empty<byte>())
            .Callback<DataStream[]>(stream =>
            {
                receivedMessage = stream;
            });

        mockEgressComponentIdService.Setup(idService => idService.ComponentId).Returns("SampleId");

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteStreams(new DataStream[] { expectedStreamMessage }, MessageAction.Default);

        // give it some time to TPL to process the message
        if (!mre.Wait(WaitTime * 10))
        {
            Assert.Fail("Did not recieve message.");
        }

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedStreamMessage, receivedMessage[0]);
        Assert.NotNull(sentMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DataMessageProcessor_WriteValue_InvalidInput(string streamId)
    {
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        var argumentExceptionThrown = false;

        try
        {
            messageProcessor.WriteValue(streamId, Classification.Dynamic, new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 }, MessageAction.Default);
        }
        catch (ArgumentException)
        {
            argumentExceptionThrown = true;
        }

        Assert.True(argumentExceptionThrown);
    }

    [Fact]
    public void DataMessageProcessor_WriteValue()
    {
        var expectedStreamId = "TestStreamId";
        var expectedDataInstance = new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 };

        StreamData[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<StreamData[]>())).Returns(Array.Empty<byte>())
            .Callback<StreamData[]>(omfData =>
            {
                receivedMessage = omfData;
            });

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteValue(expectedStreamId, Classification.Dynamic, expectedDataInstance, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedStreamId, receivedMessage[0].Id);
        Assert.Equal(expectedDataInstance, (TimeIndexedValue<int>)receivedMessage[0].Values.ToList().FirstOrDefault());
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_WriteTypeStreamAndValue_Omf20_EmitsSchemaAndInstanceMessages()
    {
        var sentMessages = new List<ISerializedOmfMessage>();

        var expectedTypeMessage = new DynamicDataType
        {
            Id = "TestType",
            Description = "TestDescription",
            Properties = new Dictionary<string, PropertyDefinition>
            {
                { "Value", new PropertyDefinition { Type = "integer", Format = "int32" } },
            },
        };

        var expectedStreamMessage = new DataStream
        {
            Id = "TestStreamId",
            Name = "Stream",
            TypeId = expectedTypeMessage.Id,
        };

        var expectedDataInstance = new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 };

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockFailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockLogManager = new Mock<ILogManager>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();

        mockEgressComponentIdService.Setup(idService => idService.ComponentId).Returns("SampleId");
        mockApplicationManifest.SetupGet(am => am.OmfVersion).Returns(OmfVersion.Omf20);

        mockFailoverDataMessageProcessor
            .Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                lock (sentMessagesLock)
                {
                    sentMessages.Add(message);
                }
            });

        using var messageProcessor = new DataMessageProcessor(
            mockLogManager.Object,
            new OmfJsonSerializer(),
            null,
            mockOmfDataEndpointManager.Object,
            mockEgressComponentIdService.Object,
            mockConfigurationProvider.Object,
            mockFailoverDataMessageProcessor.Object,
            mockApplicationManifest.Object);

        messageProcessor.WriteType(expectedTypeMessage, MessageAction.Create);
        messageProcessor.WriteStream(expectedStreamMessage, MessageAction.Create);
        messageProcessor.WriteValue(expectedStreamMessage.Id, Classification.Dynamic, expectedDataInstance, MessageAction.Default);

        // Dispose drains and flushes all pending dataflow blocks.
        messageProcessor.Dispose();

        Assert.True(SpinWait.SpinUntil(() =>
        {
            lock (sentMessagesLock)
            {
                return sentMessages.Any(x => x.MessageType == MessageType.Schema) &&
                       sentMessages.Any(x => x.MessageType == MessageType.Instance);
            }
        }, WaitTime), "Expected both schema and instance messages for OMF 2.0 path.");

        List<ISerializedOmfMessage> captured;
        lock (sentMessagesLock)
        {
            captured = sentMessages.ToList();
        }

        var schemaMessage = captured.First(x => x.MessageType == MessageType.Schema);
        var instanceMessage = captured.First(x => x.MessageType == MessageType.Instance);

        Assert.Equal(OmfVersion.Omf20, schemaMessage.OmfVersion);
        Assert.Equal(OmfVersion.Omf20, instanceMessage.OmfVersion);
        Assert.Equal(2, schemaMessage.ItemCount);
        Assert.Equal(1, instanceMessage.ItemCount);
        Assert.DoesNotContain(captured, x => x.MessageType == MessageType.Type || x.MessageType == MessageType.Container || x.MessageType == MessageType.Data);
    }

    [Fact]
    public void DataMessageProcessor_WriteStaticValue_WithExtendedProperties()
    {
        var expectedStreamId = "TestStreamId";

        StaticStreamData[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<StreamData[]>())).Returns(Array.Empty<byte>())
            .Callback<StreamData[]>(omfData =>
            {
                receivedMessage = (StaticStreamData[])omfData;
            });

        mockApplicationManifest.SetupGet(am => am.OmfVersion).Returns(OmfVersion.Omf12);

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object, mockApplicationManifest.Object);

        dynamic instance0 = new ExpandoObject();
        instance0.TankName = "Tank1";
        instance0.Serial = "FN-2187";
        messageProcessor.WriteStaticValue(expectedStreamId, null, null, instance0, null, MessageAction.Default);

        var stringProp = new PropertyDefinition { Type = "string" };
        var prop2 = new PropertyDefinition { Type = "integer", Format = "uint16" };
        var extendedProperties = new Dictionary<string, PropertyDefinition> { { "Description", stringProp }, { "ModelVersion", prop2 } };
        dynamic instance1 = new ExpandoObject();
        instance1.TankName = "Tank1";
        instance1.Serial = "FN-2187";
        instance1.Description = "Main Tank";
        instance1.ModelVersion = 44;
        messageProcessor.WriteStaticValue(expectedStreamId, extendedProperties, null, instance1, null, MessageAction.Default);

        var prop4 = new PropertyDefinition { Type = "integer", Format = "uint32" };
        var prop5 = new PropertyDefinition { Type = "number", Format = "float64", Minimum = 3.14 };
        var extendedProperties2 = new Dictionary<string, PropertyDefinition> { { "Description", stringProp }, { "ModelVersion2", prop4 }, { "Height", prop5 } };
        dynamic instance2 = new ExpandoObject();
        instance2.TankName = "Tank2";
        instance2.Serial = "FN-2187";
        instance2.Description = "Small Tank";
        instance2.ModelVersion2 = 112;
        instance2.Height = 12.511;
        messageProcessor.WriteStaticValue(expectedStreamId, extendedProperties2, null, instance2, null, MessageAction.Default);

        // Write static value with extended properties using new overload that supports OMF 1.3 fields
        var prop6 = new PropertyDefinition { Type = "string" };
        dynamic instance3 = new ExpandoObject();
        instance2.TankName = "Tank3";
        var extendedProperties3 = new Dictionary<string, PropertyDefinition> { { "ModelVersion3", stringProp } };
        messageProcessor.WriteStaticValue(expectedStreamId, expectedStreamId, "TankName", "TankDescription", "TankSource", extendedProperties3, null, instance2, null, new List<string>(["tag1"]), null, MessageAction.Default);

        Assert.True(SpinWait.SpinUntil(() => receivedMessage != null, WaitTime));

        Assert.All(receivedMessage, msg =>
        {
            Assert.Equal(expectedStreamId, msg.Id);
        });

        Assert.Null(receivedMessage[0].Properties);
        Assert.Equal(2, receivedMessage[1].Properties.Count);
        Assert.Equal(3, receivedMessage[2].Properties.Count);

        Assert.True(receivedMessage[1].Properties.ContainsKey("Description"));
        Assert.True(receivedMessage[1].Properties.ContainsKey("ModelVersion"));

        Assert.True(receivedMessage[2].Properties.ContainsKey("Description"));
        Assert.True(receivedMessage[2].Properties.ContainsKey("Description"));
        Assert.True(receivedMessage[2].Properties.ContainsKey("ModelVersion2"));
        Assert.True(receivedMessage[2].Properties.ContainsKey("Height"));

        Assert.Equal("TankName", receivedMessage[3].Name);
        Assert.Equal("TankDescription", receivedMessage[3].Description);
        Assert.Equal("TankSource", receivedMessage[3].DataSource);
        Assert.Equal(["tag1"], receivedMessage[3].Tags);

        Assert.Equal("uint16", receivedMessage[1].Properties["ModelVersion"].Format);
        Assert.Equal(3.14, receivedMessage[2].Properties["Height"].Minimum);

        var msg1Values = DeserializeValues(receivedMessage[1].Values);
        Assert.Equal("Tank1", msg1Values["TankName"].ToString());

        var msg2Values = DeserializeValues(receivedMessage[2].Values);
        Assert.Equal("12.511", msg2Values["Height"].ToString());

        var msg3Values = DeserializeValues(receivedMessage[3].Values);
        Assert.Equal("Tank3", msg3Values["TankName"].ToString());

        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_WriteStaticValue_WithRelationships_Omf20_IncludesRelationshipsInInstancePayload()
    {
        using var mre = new ManualResetEventSlim(false);

        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        var mockLogManager = new Mock<ILogManager>();

        ICollection<string> getErrors = null;
        var bufferingConfiguration = new BufferingConfiguration()
        {
            BufferLocation = "TestLocation",
            MaxBufferSizeMB = 40,
            MaxDataBulkTime = TimeSpan.FromMilliseconds(100),
        };
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out bufferingConfiguration, out getErrors)).Returns(true);

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                if (message.MessageType == MessageType.Instance)
                {
                    sentMessage = message;
                    mre.Set();
                }
            });

        mockApplicationManifest.SetupGet(am => am.OmfVersion).Returns(OmfVersion.Omf20);

        try
        {
            using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, new OmfJsonSerializer(), null,
                mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object, mockApplicationManifest.Object);

            dynamic instance = new ExpandoObject();
            instance.Name = "Asset-1";

            var relationships = new List<Link>
            {
                new Link(new RelationshipLinkNode("source-1"), new RelationshipLinkNode("target-1")),
            };

            messageProcessor.WriteStaticValue("AssetType", "Asset-1", "Asset One", "Test asset", "Simulator", null, null, instance, null, null, relationships, MessageAction.Create);

            Thread.Sleep(250);
        }
        catch (AggregateException)
        {
            // The mockLogManager causes a null reference exception at the disposing of the messageProcessor, but the messageProcessor and its components are still disposed.
        }

        Assert.True(mre.Wait(WaitTime), "Did not receive OMF instance message.");
        Assert.NotNull(sentMessage);
        Assert.Equal(MessageType.Instance, sentMessage.MessageType);

        using var json = JsonDocument.Parse(sentMessage.MessageBody);
        Assert.True(json.RootElement.TryGetProperty("relationships", out var relationshipsElement));
        Assert.Equal(JsonValueKind.Array, relationshipsElement.ValueKind);
        Assert.Equal(1, relationshipsElement.GetArrayLength());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DataMessageProcessor_WriteStaticValue_Delete_Omf20_OmitsTypeIdFromPayload(string typeId)
    {
        using var mre = new ManualResetEventSlim(false);

        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        var mockLogManager = new Mock<ILogManager>();

        ICollection<string> getErrors = null;
        var bufferingConfiguration = new BufferingConfiguration()
        {
            BufferLocation = "TestLocation",
            MaxBufferSizeMB = 40,
            MaxDataBulkTime = TimeSpan.FromMilliseconds(100),
        };
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out bufferingConfiguration, out getErrors)).Returns(true);

        mockOmfDataEndpointManager.Setup(em => em.SendMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                if (message.MessageType == MessageType.Instance)
                {
                    sentMessage = message;
                    mre.Set();
                }
            });

        mockApplicationManifest.SetupGet(am => am.OmfVersion).Returns(OmfVersion.Omf20);

        try
        {
            using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, new OmfJsonSerializer(), null,
                mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, null, mockApplicationManifest.Object);

            dynamic instance = new ExpandoObject();
            instance.Name = "Asset-1";

            messageProcessor.WriteStaticValue(typeId, "Asset-1", "Asset One", "Test asset", "Simulator", instance, null, null, null, MessageAction.Delete);

            Thread.Sleep(250);
        }
        catch (AggregateException)
        {
            // The mockLogManager causes a null reference exception at the disposing of the messageProcessor, but the messageProcessor and its components are still disposed.
        }

        Assert.True(mre.Wait(WaitTime), "Did not receive OMF instance message.");
        Assert.NotNull(sentMessage);
        Assert.Equal(MessageType.Instance, sentMessage.MessageType);
        Assert.Equal(MessageAction.Delete, sentMessage.MessageAction);

        using var json = JsonDocument.Parse(sentMessage.MessageBody);
        Assert.True(json.RootElement.TryGetProperty("entities", out var entitiesElement));
        Assert.Equal(JsonValueKind.Array, entitiesElement.ValueKind);
        Assert.Equal(1, entitiesElement.GetArrayLength());

        var entity = entitiesElement[0];

        // Delete payload must retain the instance id but must not carry a typeid.
        Assert.True(entity.TryGetProperty("id", out var idElement));
        Assert.Equal("Asset-1", idElement.GetString());
        Assert.False(entity.TryGetProperty("typeid", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DataMessageProcessor_WriteStaticValue_Delete_AllowsNullTypeId(string typeId)
    {
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        var instance = new Dictionary<string, string> { { "prop1", "prop1Value" } };

        // Neither overload should throw when a delete provides no typeId.
        messageProcessor.WriteStaticValue(typeId, "Asset-1", "Asset One", "Test asset", "Simulator", instance, null, null, null, MessageAction.Delete);
        messageProcessor.WriteStaticValue(typeId, "Asset-1", "Asset One", "Test asset", "Simulator", null, null, instance, null, null, null, MessageAction.Delete);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DataMessageProcessor_WriteStaticValue_NonDelete_NullTypeId_Throws(string typeId)
    {
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        var instance = new Dictionary<string, string> { { "prop1", "prop1Value" } };

        Assert.ThrowsAny<ArgumentException>(() => messageProcessor.WriteStaticValue(typeId, "Asset-1", "Asset One", "Test asset", "Simulator", instance, null, null, null, MessageAction.Create));
        Assert.ThrowsAny<ArgumentException>(() => messageProcessor.WriteStaticValue(typeId, "Asset-1", "Asset One", "Test asset", "Simulator", null, null, instance, null, null, null, MessageAction.Update));
    }

    [Fact]
    public void DataMessageProcessor_Relationships_CanBeReusedAcrossSchemaInstanceAndIndependentMessages_Omf20()
    {
        var sentMessages = new ConcurrentBag<ISerializedOmfMessage>();

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockApplicationManifest = new Mock<IApplicationManifest>();
        var mockLogManager = new Mock<ILogManager>();

        ICollection<string> getErrors = null;
        var bufferingConfiguration = new BufferingConfiguration()
        {
            BufferLocation = "TestLocation",
            MaxBufferSizeMB = 40,
            MaxDataBulkTime = TimeSpan.FromMilliseconds(100),
        };
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out bufferingConfiguration, out getErrors)).Returns(true);

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessages.Add(message);
            });

        mockApplicationManifest.SetupGet(am => am.OmfVersion).Returns(OmfVersion.Omf20);

        try
        {
            using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, new OmfJsonSerializer(), null,
                mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object, mockApplicationManifest.Object);

            var reusableLink = new Link(
                new RelationshipLinkNode("RootAsset", collection: Tokens.TypesCollection),
                new RelationshipLinkNode("AssetType", collection: Tokens.TypesCollection));

            var dataType = new StaticDataType
            {
                Id = "AssetType",
                Properties = new Dictionary<string, PropertyDefinition>
                {
                    ["AssetId"] = new PropertyDefinition { Type = "string", IsIndex = true },
                },
                Relationships = new List<Link> { reusableLink },
            };

            dynamic staticInstance = new ExpandoObject();
            staticInstance.AssetId = "Asset-1";

            dynamic eventInstance = new ExpandoObject();
            eventInstance.Value = 42;

            messageProcessor.WriteType(dataType, MessageAction.Create);
            messageProcessor.WriteSchemaRelationship(reusableLink, MessageAction.Create);
            messageProcessor.WriteStaticValue("AssetType", "Asset-1", "Asset One", "Desc", "Simulator", null, null, staticInstance, null, null, new List<Link> { reusableLink }, MessageAction.Create);
            messageProcessor.WriteEvent("Event-1", "EventType", "Adapter Start", "Start Event", "Simulator", DateTime.UtcNow, null, null, null, eventInstance, null, null, new List<Link> { reusableLink }, MessageAction.Create);
            messageProcessor.WriteInstanceRelationship(reusableLink, MessageAction.Create);

            Thread.Sleep(500);
        }
        catch (AggregateException)
        {
            // The mockLogManager causes a null reference exception at the disposing of the messageProcessor, but the messageProcessor and its components are still disposed.
        }

        Assert.True(SpinWait.SpinUntil(() => sentMessages.Any(m => m.MessageType == MessageType.Schema), WaitTime));
        Assert.True(SpinWait.SpinUntil(() => sentMessages.Any(m => m.MessageType == MessageType.Instance), WaitTime));

        var schemaRelationshipCount = sentMessages
            .Where(m => m.MessageType == MessageType.Schema)
            .Sum(m => GetArrayPropertyCount(m.MessageBody, "relationships"));

        var instanceRelationshipCount = sentMessages
            .Where(m => m.MessageType == MessageType.Instance)
            .Sum(m => GetArrayPropertyCount(m.MessageBody, "relationships"));

        var instanceEntityCount = sentMessages
            .Where(m => m.MessageType == MessageType.Instance)
            .Sum(m => GetArrayPropertyCount(m.MessageBody, "entities"));

        var instanceEventCount = sentMessages
            .Where(m => m.MessageType == MessageType.Instance)
            .Sum(m => GetArrayPropertyCount(m.MessageBody, "events"));

        Assert.True(schemaRelationshipCount >= 2, "Expected schema relationships from both type-linked and independent relationship writes.");
        Assert.True(instanceRelationshipCount >= 3, "Expected instance relationships from entity/event inline relationships plus independent relationship write.");
        Assert.True(instanceEntityCount >= 1, "Expected at least one entity in instance payload.");
        Assert.True(instanceEventCount >= 1, "Expected at least one event in instance payload.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DataMessageProcessor_WriteStaticValue_WithExtendedProperties_InvalidInput(string streamId)
    {
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        Assert.ThrowsAny<ArgumentException>(() => messageProcessor.WriteValues(streamId, Classification.Dynamic, new List<TimeIndexedValue<int>>(), MessageAction.Default));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DataMessageProcessor_WriteValues_InvalidInput(string streamId)
    {
        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        var argumentExceptionThrown = false;

        try
        {
            messageProcessor.WriteValues(streamId, Classification.Dynamic, new List<TimeIndexedValue<int>>(), MessageAction.Default);
        }
        catch (ArgumentException)
        {
            argumentExceptionThrown = true;
        }

        Assert.True(argumentExceptionThrown);
    }

    [Fact]
    public void DataMessageProcessor_WriteValues_FailoverMessageProcessor()
    {
        var expectedStreamId = "TestStreamId";

        var expectedDataInstances = new List<TimeIndexedValue<int>>();
        for (int i = 0; i < 100; i++)
        {
            expectedDataInstances.Add(new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = i });
        }

        StreamData[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<StreamData[]>())).Returns(Array.Empty<byte>())
            .Callback<StreamData[]>(omfData =>
            {
                receivedMessage = omfData;
            });

        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteValues(expectedStreamId, Classification.Dynamic, expectedDataInstances, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedStreamId, receivedMessage[0].Id);
        Assert.Equal(expectedDataInstances.Count, receivedMessage[0].Values.Count());
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_WriteValues_OmfDataMessageProcessor()
    {
        var expectedStreamId = "TestStreamId";

        var expectedDataInstances = new List<TimeIndexedValue<int>>();
        for (int i = 0; i < 100; i++)
        {
            expectedDataInstances.Add(new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = i });
        }

        StreamData[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManger = new Mock<ILogManager>();

        mockOmfDataEndpointManager.Setup(em => em.SendMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<StreamData[]>())).Returns(Array.Empty<byte>())
            .Callback<StreamData[]>(omfData =>
            {
                receivedMessage = omfData;
            });

        using var messageProcessor = new DataMessageProcessor(mockLogManger.Object, mockSerializer.Object, null,
            mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, null);

        messageProcessor.WriteValues(expectedStreamId, Classification.Dynamic, expectedDataInstances, MessageAction.Default);

        // give it some time to TPL to process the message
        Thread.Sleep(WaitTime);

        Assert.NotNull(receivedMessage);
        Assert.Equal(expectedStreamId, receivedMessage[0].Id);
        Assert.Equal(expectedDataInstances.Count, receivedMessage[0].Values.Count());
        Assert.NotNull(sentMessage);
    }

    [Fact]
    public void DataMessageProcessor_MaxDataBulkTime_FlushOperation()
    {
        StreamData[] receivedMessage = null;
        ISerializedOmfMessage sentMessage = null;

        var mockEgressComponentIdService = new Mock<IEgressComponentIdService>();
        var mockConfigurationProvider = new Mock<IConfigurationProvider>();
        var mockfailoverDataMessageProcessor = new Mock<IFailoverDataMessageProcessor>();
        var mockOmfDataEndpointManager = new Mock<IOmfDataEndpointManager>();
        var mockSerializer = new Mock<ISerializer>();
        var mockLogManager = new Mock<ILogManager>();

        mockfailoverDataMessageProcessor.Setup(em => em.ProcessOmfMessage(It.IsAny<ISerializedOmfMessage>()))
            .Callback<ISerializedOmfMessage>(message =>
            {
                sentMessage = message;
            });

        mockSerializer.Setup(serializer => serializer.Serialize(It.IsAny<StreamData[]>())).Returns(Array.Empty<byte>())
            .Callback<StreamData[]>(omfData =>
            {
                receivedMessage = omfData;
            });

        ICollection<string> getErrors = null;
        var dataInstance = new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 42 };
        var flushTime = 200;
        var configuration = new BufferingConfiguration()
        {
            BufferLocation = "TestLocation",
            MaxBufferSizeMB = 40,
            MaxDataBulkTime = TimeSpan.FromMilliseconds(flushTime),
        };
        mockConfigurationProvider.Setup(x => x.TryGetConfiguration(It.IsAny<string>(), It.IsAny<string>(), out configuration, out getErrors)).Returns(true);
        using var messageProcessor = new DataMessageProcessor(mockLogManager.Object, mockSerializer.Object, null, mockOmfDataEndpointManager.Object, mockEgressComponentIdService.Object, mockConfigurationProvider.Object, mockfailoverDataMessageProcessor.Object);

        messageProcessor.WriteValue("TestStreamId", Classification.Dynamic, dataInstance, MessageAction.Default);

        // delaying for flushTime - 20ms and didn't receive a message
        Thread.Sleep(flushTime - 20);
        Assert.Null(receivedMessage);

        // delaying for flushTime + 10ms and received a message
        Thread.Sleep(20000);
        Assert.NotNull(receivedMessage);
    }

    private static Dictionary<string, object> DeserializeValues(IEnumerable<object> values)
    {
        var serialize = System.Text.Json.JsonSerializer.Serialize(values);
        serialize = serialize.TrimStart('[');
        serialize = serialize.TrimEnd(']');
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(serialize);
    }

    private static int GetArrayPropertyCount(byte[] body, string propertyName)
    {
        using var json = JsonDocument.Parse(body);

        if (json.RootElement.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            return array.GetArrayLength();
        }

        return 0;
    }
}
