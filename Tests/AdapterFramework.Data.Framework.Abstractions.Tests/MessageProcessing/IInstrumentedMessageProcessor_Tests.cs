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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.Abstractions.Tests.MessageProcessing;

public class IInstrumentedMessageProcessor_Tests
{
    [Fact]
    public void DefaultAssetAndEventWriteCountMembers_PreserveLegacyImplementations_Test()
    {
        IInstrumentedMessageProcessor messageProcessor = new LegacyInstrumentedMessageProcessor();

        Assert.Equal(0, messageProcessor.GetAssetCount());
        Assert.Equal(0, messageProcessor.GetEventWriteCount());
    }

    private class LegacyInstrumentedMessageProcessor : IInstrumentedMessageProcessor
    {
        public void SetStreamIdPrefix(string streamIdPrefix)
        {
        }

        public int GetStreamCount() => 0;

        public int GetTypeCount() => 0;

        public long GetAndResetEventsCounter() => 0;

        public void ClearCounters()
        {
        }

        public void WriteType(DataType dataType, MessageAction messageAction = MessageAction.Default)
        {
        }

        public void WriteTypes(DataType[] dataTypes, MessageAction messageAction = MessageAction.Default)
        {
        }

        public void WriteStream(DataStream dataStream, MessageAction messageAction = MessageAction.Default)
        {
        }

        public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction = MessageAction.Default)
        {
        }

        public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction = MessageAction.Default) where T : class
        {
        }

        public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null) where T : class
        {
        }

        public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default) where T : class
        {
        }

        public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction = MessageAction.Default, PartitionKey? partitionKey = null) where T : class
        {
        }

        public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, MessageAction messageAction = MessageAction.Default) where T : class
        {
        }

        public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class
        {
        }

        public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default) where T : class
        {
        }

        public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class
        {
        }

        public void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default)
        {
        }

        public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default)
        {
        }
    }
}
