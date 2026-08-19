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
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;
using AdapterFramework.Data.Framework.MessageProcessor.DataFilters;
using AdapterFramework.Data.Framework.Tests.Helper;
using AdapterFramework.Data.DataModel;
using Xunit;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Messages;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.HistoryRecovery;

public class HistoryRecoveryAdapterMessageProcessor_Tests
{
    [Fact]
    public void HistoryRecoveryAdapterMessageProcessor_Constructor_Throws_Test()
    {
        Assert.ThrowsAny<ArgumentException>(() => new HistoryRecoveryAdapterMessageProcessor(null));
    }

    [Fact]
    public void HistoryRecoveryAdapterMessageProcessor_WriteValue_Test()
    {
        var mockMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var messageProcessor = new HistoryRecoveryAdapterMessageProcessor(mockMessageProcessor.Object);

        long eventCount = 0;
        var updateAction = new Action<long>((long input) => { eventCount = input; });
        messageProcessor.SetEventCountUpdateAction(updateAction);

        var dataSelectionItem = new TestDataSelectionItem("Test")
        {
            DataFilterId = "DataFilterId",
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };

        messageProcessor.WriteDynamicValue(dataSelectionItem, new TimeIndexedValue<int>(), MessageAction.Create);

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void HistoryRecoveryAdapterMessageProcessor_WriteValues_Test()
    {
        var mockMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var messageProcessor = new HistoryRecoveryAdapterMessageProcessor(mockMessageProcessor.Object);

        long eventCount = 0;
        var updateAction = new Action<long>((long input) => { eventCount = input; });
        messageProcessor.SetEventCountUpdateAction(updateAction);

        var dataSelectionItem = new TestDataSelectionItem("Test")
        {
            DataFilterId = "DataFilterId",
            DataFilterCache = new DataFilterCachedValues<TimeIndexedValue<int>>(),
        };

        var values = new List<TimeIndexedValue<int>>() { new TimeIndexedValue<int>(), new TimeIndexedValue<int>(), new TimeIndexedValue<int>() };
        messageProcessor.WriteDynamicValues(dataSelectionItem, values, MessageAction.Create);

        Assert.Equal(values.Count, eventCount);
    }

    [Fact]
    public void HistoryRecoveryAdapterMessageProcessor_WriteValues_OMF20_DoesNotThrow()
    {
        var mockMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var messageProcessor = new HistoryRecoveryAdapterMessageProcessor(mockMessageProcessor.Object, OmfVersion.Omf20);

        var entityItem = new StaticStreamData
        {
            InstanceId = "TestEntityInstanceId",
            Id = "TestEntityTypeId",
            Name = "Name",
            DataSource = "DataSource",
            Description = "Description",
            Value = new Dictionary<string, object>(),
            Properties = new Dictionary<string, PropertyDefinition>(),
            PropertyOverrides = new Dictionary<string, PropertyDefinitionOverride>(),
        };

        var instanceRelationshipItem = new Link(new DataStreamLinkNode("SourceId"), new DataStreamLinkNode("TargetId"));

        var typeRelationshipItem = new Link(new DataTypeLinkNode("SourceId", "0"), new DataTypeLinkNode("TargetId", "0"));

        var eventItem = new Event
        {
            Id = "TestEventId",
            TypeId = "TestEventTypeId",
            Name = "Name",
            Description = "Description",
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddMinutes(5),
            Properties = new Dictionary<string, PropertyDefinition>(),
            PropertyOverrides = new Dictionary<string, PropertyDefinitionOverride>(),
            Value = new Dictionary<string, object>(),
        };

        try
        {
            messageProcessor.WriteStaticValue(entityItem.Id, entityItem.InstanceId, entityItem.Name, entityItem.Description, entityItem.DataSource, entityItem.Value);
            messageProcessor.WriteStaticValue(entityItem.Id, entityItem.InstanceId, entityItem.Name, entityItem.Description, entityItem.DataSource, entityItem.Properties, entityItem.PropertyOverrides, entityItem.Value);
            messageProcessor.WriteInstanceRelationship(instanceRelationshipItem);
            messageProcessor.WriteTypeRelationship(typeRelationshipItem);
            messageProcessor.WriteEvent(eventItem.TypeId, eventItem.Id, eventItem.Name, eventItem.Description, eventItem.StartTime, eventItem.EndTime, eventItem.Properties, eventItem.PropertyOverrides, eventItem.Value);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Exception related to OMFVersion was thrown when writing static value, instance relationship, type relationship or event with OMF 2.0: {ex}");
        }
    }
}
