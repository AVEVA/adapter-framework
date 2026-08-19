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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.Health;

public abstract class HealthOmfMessageCreatorBase
{
    public const string NextHealthMessageExpected = "NextHealthMessageExpected";
    public const string DeviceStatus = "DeviceStatus";
    public const string Time = "Time";

    private readonly LinkNode _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthOmfMessageCreatorBase"/> class.
    /// </summary>
    /// <param name="parent">Parent link node used to link the health structure to.</param>
    protected HealthOmfMessageCreatorBase(LinkNode parent)
    {
        _parent = parent;
    }

    private enum DynamicDataTypes
    {
        Integer,
        Double,
        DateTime,
        String,
    }

    public static DataStream[] AddStreamMetadata(DataStream[] dataStreams, string componentId, string componentType)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentType, nameof(componentType));

        if (componentType.Equals(EdgeSystemConstants.OmfEgressComponentType, StringComparison.InvariantCultureIgnoreCase)
            || componentType.Equals(EdgeSystemConstants.FailoverComponentType, StringComparison.InvariantCultureIgnoreCase))
        {
            return dataStreams;
        }

        var metaDataDictionary = new Dictionary<string, object>
        {
            { EdgeSystemConstants.DataSourceString, componentId },
            { EdgeSystemConstants.AdapterTypeString, componentType },
        };

        foreach (var dataStream in dataStreams)
        {
            if (dataStream.Metadata == null)
            {
                dataStream.Metadata = metaDataDictionary;
            }
            else
            {
                foreach (var (key, value) in metaDataDictionary)
                {
                    dataStream.Metadata[key] = value;
                }
            }
        }

        return dataStreams;
    }

    public void CreateAndSendHealthStructure(IHealthMessageProcessor messageProcessor, string componentId, string componentType)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentType, nameof(componentType));

        messageProcessor.WriteHealthTypes(GetTypes());

        foreach (var (id, classification, instance) in GetAssets())
        {
            messageProcessor.WriteHealthValue(id, classification, instance);
        }

        messageProcessor.WriteHealthStreams(AddStreamMetadata(GetStreams(), componentId, componentType));

        SendLinks(messageProcessor);
    }

    public void SendLinks(IHealthMessageProcessor messageProcessor)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));

        foreach (var (id, classification, instance) in GetLinks())
        {
            messageProcessor.WriteHealthValue(id, classification, instance);
        }
    }

    /// <summary>
    /// Gets the necessary information to send an OMF heartbeat message.
    /// </summary>
    /// <param name="span">The interval between heartbeats.</param>
    /// <returns>A value tuple containing the required information to send an OMF Data message.</returns>
    public (string Id, Classification Classification, object Values) GetHeartBeatData(TimeSpan span)
    {
        var values = new Dictionary<string, object>
        {
            [Time] = DateTime.UtcNow,
            [NextHealthMessageExpected] = DateTime.UtcNow.Add(span),
        };

        return (GetHeartbeatStreamId(), Classification.Dynamic, values);
    }

    /// <summary>
    /// Gets the necessary information needed to send an OMF device status message.
    /// </summary>
    /// <param name="status">The status of the connection to the device.</param>
    /// <returns>a value tuple containing the required information to send an OMF data message.</returns>
    public (string Id, Classification Classification, object Values) GetDeviceStatusData(string status)
    {
        var values = new Dictionary<string, object>
        {
            [Time] = DateTime.UtcNow,
            [DeviceStatus] = status,
        };
        return (GetDeviceStatusStreamId(), Classification.Dynamic, values);
    }

    public abstract LinkNode GetComponentLink();
    protected abstract string GetDeviceStatusStreamId();
    protected abstract string GetHeartbeatStreamId();
    protected abstract string GetComponentAssetId();
    protected abstract DataType GetComponentType();
    protected abstract (string Id, Classification Classification, object Values) GetComponentAsset();

    private static DynamicDataType CreateDynamicType(string typeId, DynamicDataTypes dataType, string timestampIndexName = Time, string typeIndex = null)
    {
        var dynamicType = new DynamicDataType
        {
            Id = typeId,
        };
        var timestampProperty = new PropertyDefinition
        {
            IsIndex = true,
            Type = Tokens.StringToken,
            Format = Tokens.DateTimeToken,
        };

        var valueProperty = new PropertyDefinition();

        switch (dataType)
        {
            case DynamicDataTypes.Double:
                valueProperty.Type = Tokens.NumberToken;
                valueProperty.Format = Tokens.Float64Token;
                break;

            case DynamicDataTypes.Integer:
                valueProperty.Type = Tokens.IntegerToken;
                valueProperty.Format = Tokens.Int32Token;
                break;

            case DynamicDataTypes.DateTime:
                valueProperty.Type = Tokens.StringToken;
                valueProperty.Format = Tokens.DateTimeToken;
                break;

            case DynamicDataTypes.String:
                valueProperty.Type = Tokens.StringToken;
                break;
        }

        if (typeIndex == null)
        {
            typeIndex = typeId;
        }

        dynamicType.Properties = new Dictionary<string, PropertyDefinition>
        {
            [timestampIndexName] = timestampProperty,
            [typeIndex] = valueProperty,
        };

        return dynamicType;
    }

    private DataStream[] GetStreams()
    {
        return
        [

            // Device Status
            new DataStream()
            {
                Id = GetDeviceStatusStreamId(),
                TypeId = DeviceStatus,
                Name = DeviceStatus,
            },

            // Next Health Message Expected
            new DataStream()
            {
                Id = GetHeartbeatStreamId(),
                TypeId = NextHealthMessageExpected,
                Name = NextHealthMessageExpected,
            },
        ];
    }

    private DataType[] GetTypes()
    {
        return
        [
            GetComponentType(),
            CreateDynamicType(DeviceStatus, DynamicDataTypes.String),
            CreateDynamicType(NextHealthMessageExpected, DynamicDataTypes.DateTime),
        ];
    }

    private List<(string Id, Classification Classification, object Values)> GetAssets() =>
        new List<(string, Classification, object)> { GetComponentAsset() };

    private List<(string Id, Classification Classification, object Values)> GetLinks()
    {
        var links = new List<(string, Classification, object)>();

        // link adapter component health asset to adapter instance health asset
        var sourceLink = _parent;
        var targetLink = GetComponentLink();
        var link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        sourceLink = targetLink;

        // link adapter heartbeat to adapter component health asset
        targetLink = new DataStreamLinkNode(GetHeartbeatStreamId());
        link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        // link adapter device status to adapter component health asset
        targetLink = new DataStreamLinkNode(GetDeviceStatusStreamId());
        link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        return links;
    }
}
