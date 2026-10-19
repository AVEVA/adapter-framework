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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Messages;
using Xunit;

namespace AdapterFramework.Data.Framework.DataFlow.Tests;

public class SchemaMessage_Tests
{
    [Fact]
    public void SchemaMessage_Constructors_PreserveLegacyAndKeyedCallShapes_Test()
    {
        DataType[] types = [new DynamicDataType { Id = "type" }];
        DataStream[] streams = [new DataStream { Id = "stream" }];
        Link[] relationships = [new Link(new DataTypeLinkNode("source", null), new DataTypeLinkNode("target", null))];

        using var legacy = new SchemaMessage(types, streams, relationships, 1, 1, 1, MessageAction.Create);
        using var explicitPool = new SchemaMessage(types, streams, relationships, 1, 1, 1, MessageAction.Create, false);
        using var namedKey = new SchemaMessage(types, streams, relationships, 1, 1, 1, MessageAction.Create, partitionKey: PartitionKey.Key1);
        using var explicitKey = new SchemaMessage(types, streams, relationships, 1, 1, 1, MessageAction.Create, false, PartitionKey.Key2);
        using var nullKey = new SchemaMessage(types, streams, relationships, 1, 1, 1, MessageAction.Create, partitionKey: null);

        Assert.Null(legacy.PartitionKey);
        Assert.Null(explicitPool.PartitionKey);
        Assert.Null(nullKey.PartitionKey);
        Assert.Equal(PartitionKey.Key1, namedKey.PartitionKey);
        Assert.Equal(PartitionKey.Key2, explicitKey.PartitionKey);
        Assert.Same(types, namedKey.Types);
        Assert.Same(streams, namedKey.Containers);
        Assert.Same(relationships, namedKey.Relationships);
        Assert.Equal(1, namedKey.TypeCount);
        Assert.Equal(1, namedKey.ContainerCount);
        Assert.Equal(1, namedKey.RelationshipCount);
        Assert.Equal(MessageAction.Create, namedKey.MessageAction);
    }

    [Fact]
    public void SchemaMessage_Constructors_PreserveLegacyBinarySignatures_Test()
    {
        Assert.NotNull(typeof(SchemaMessage).GetConstructor(
            [typeof(DataType[]), typeof(DataStream[]), typeof(Link[]), typeof(int), typeof(int), typeof(int), typeof(MessageAction), typeof(bool)]));
        Assert.NotNull(typeof(RelationshipMessage).GetConstructor([typeof(Link), typeof(MessageAction)]));

        var link = new Link(new DataTypeLinkNode("source", null), new DataTypeLinkNode("target", null));
        var legacy = new RelationshipMessage(link, MessageAction.Create);
        var keyed = new RelationshipMessage(link, MessageAction.Create, PartitionKey.Key1);

        Assert.Same(link, legacy.Relationship);
        Assert.Equal(MessageAction.Create, legacy.MessageAction);
        Assert.Null(legacy.PartitionKey);
        Assert.Equal(PartitionKey.Key1, keyed.PartitionKey);
    }
}
