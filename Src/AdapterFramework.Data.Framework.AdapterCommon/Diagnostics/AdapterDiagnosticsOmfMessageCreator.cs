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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Common.Diagnostics.Events;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Common.Constants.DiagnosticsConstants;

namespace AdapterFramework.Data.Framework.AdapterCommon.Diagnostics;

public class AdapterDiagnosticsOmfMessageCreator 
{
    #region Private Fields

    private readonly LinkNode _assetNode;
    private readonly string _streamIdPrefix;
    private readonly OmfVersion _omfVersion;

    #endregion

    #region Constructors

    public AdapterDiagnosticsOmfMessageCreator(string componentId, string streamIdPrefix, LinkNode assetNode)
        : this(componentId, streamIdPrefix, assetNode, OmfVersion.Omf12)
    {
    }

    public AdapterDiagnosticsOmfMessageCreator(string componentId, string streamIdPrefix, LinkNode assetNode, OmfVersion omfVersion)
    {
        ThrowHelper.ThrowIfArgumentNull(assetNode, nameof(assetNode));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        _streamIdPrefix = streamIdPrefix != null 
            ? $"{streamIdPrefix}{componentId}" 
            : componentId;

        _assetNode = assetNode;
        _omfVersion = omfVersion;
    }

    #endregion

    #region Public Methods

    public void CreateAndSendStructure(IDiagnosticsMessageProcessor messageProcessor, string componentId, string componentType)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));

        messageProcessor.WriteDiagnosticsTypes(GetTypes());

        messageProcessor.WriteDiagnosticsStreams(HealthOmfMessageCreatorBase.AddStreamMetadata(GetStreams(), componentId, componentType));

        foreach (var (id, classification, instance) in GetLinks())
        {
            messageProcessor.WriteDiagnosticsValue(id, classification, instance);
        }
    }

    public string GetIoRateStreamId() => $"{_streamIdPrefix}.{IoRateStreamName}";

    public string GetStreamCountStreamId() => $"{_streamIdPrefix}.{StreamCountStreamName}";

    public string GetAssetCountStreamId() => $"{_streamIdPrefix}.{AssetCountStreamName}";

    public string GetEventWriteCountStreamId() => $"{_streamIdPrefix}.{EventWriteCountStreamName}";

    public string GetErrorRateStreamId() => $"{_streamIdPrefix}.{ErrorRateStreamName}";

    private DataType[] GetTypes()
    {
        var errorRateType = new[] { GetErrorRateType() };
        var otherTypes = GetMessageProcessorDiagnosticsTypes();
        return [.. errorRateType.Union(otherTypes)];
    }

    private DataType[] GetMessageProcessorDiagnosticsTypes()
    {
        var timestampProperty = new PropertyDefinition
        {
            IsIndex = true,
            Type = Tokens.StringToken,
            Format = Tokens.DateTimeToken,
        };
        var integerProperty = new PropertyDefinition
        {
            Type = Tokens.IntegerToken,
            Format = Tokens.Int32Token,
        };
        var longProperty = new PropertyDefinition
        {
            Type = Tokens.IntegerToken,
            Format = Tokens.Int64Token,
        };
        var doubleProperty = new PropertyDefinition
        {
            Type = Tokens.NumberToken,
            Format = Tokens.Float64Token,
        };

        var streamCountDiagnosticsType = new DynamicDataType
        {
            Id = StreamCountTypeId,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [nameof(StreamCountEvent.Timestamp)] = timestampProperty,
                [nameof(StreamCountEvent.StreamCount)] = integerProperty,
                [nameof(StreamCountEvent.TypeCount)] = integerProperty,
            },
        };

        var assetCountDiagnosticsType = new DynamicDataType
        {
            Id = AssetCountTypeId,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [nameof(AssetCountEvent.Timestamp)] = timestampProperty,
                [nameof(AssetCountEvent.AssetCount)] = integerProperty,
            },
        };

        var eventWriteCountDiagnosticsType = new DynamicDataType
        {
            Id = EventWriteCountTypeId,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [nameof(EventWriteCountEvent.Timestamp)] = timestampProperty,
                [nameof(EventWriteCountEvent.EventWriteCount)] = longProperty,
            },
        };

        var dataRateDiagnosticsType = new DynamicDataType
        {
            Id = IoRateTypeId,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [nameof(IoRateEvent.Timestamp)] = timestampProperty,
                [nameof(IoRateEvent.IORate)] = doubleProperty,
            },
        };

        // AssetCount and EventWriteCount are OMF 2.0 only concepts, so they are not published for earlier OMF versions.
        return _omfVersion == OmfVersion.Omf20
            ? new DataType[] { streamCountDiagnosticsType, assetCountDiagnosticsType, eventWriteCountDiagnosticsType, dataRateDiagnosticsType }
            : new DataType[] { streamCountDiagnosticsType, dataRateDiagnosticsType };
    }

    private static DataType GetErrorRateType()
    {
        var timestampProperty = new PropertyDefinition
        {
            IsIndex = true,
            Type = Tokens.StringToken,
            Format = Tokens.DateTimeToken,
        };

        var errorRateProperty = new PropertyDefinition
        {
            Type = Tokens.NumberToken,
            Format = Tokens.Float64Token,
        };

        var systemDiagnosticsType = new DynamicDataType
        {
            Id = ErrorRateTypeId,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [nameof(ErrorRateEvent.Timestamp)] = timestampProperty,
                [nameof(ErrorRateEvent.ErrorRate)] = errorRateProperty,
            },
        };

        return systemDiagnosticsType;
    }

    private IEnumerable<ValueTuple<string, Classification, object>> GetLinks()
    {
        var links = new List<(string, Classification, object)>();
        var sourceLink = _assetNode;

        // Link adapter error rate to adapter component health asset
        var targetLink = new DataStreamLinkNode(GetErrorRateStreamId());
        var link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        // Link adapter IO rate to adapter component health asset
        targetLink = new DataStreamLinkNode(GetIoRateStreamId());
        link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        // Link adapter stream count to adapter component health asset
        targetLink = new DataStreamLinkNode(GetStreamCountStreamId());
        link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        // AssetCount and EventWriteCount are OMF 2.0 only concepts, so they are not linked for earlier OMF versions.
        if (_omfVersion == OmfVersion.Omf20)
        {
            // Link adapter asset count to adapter component health asset
            targetLink = new DataStreamLinkNode(GetAssetCountStreamId());
            link = new Link(sourceLink, targetLink);
            links.Add((Tokens.Link, Classification.Static, link));

            // Link adapter event write count to adapter component health asset
            targetLink = new DataStreamLinkNode(GetEventWriteCountStreamId());
            link = new Link(sourceLink, targetLink);
            links.Add((Tokens.Link, Classification.Static, link));
        }

        return links;
    }

    private DataStream[] GetStreams()
    {
        var errorRateStream = new[] { GetErrorRateStream() };
        var otherStreams = GetMessageProcessorDiagnosticsStream();
        return [.. errorRateStream.Union(otherStreams)];
    }

    private DataStream GetErrorRateStream()
    {
        return new DataStream
        {
            Id = GetErrorRateStreamId(),
            TypeId = ErrorRateTypeId,
            Name = ErrorRateStreamName,
        };
    }

    private DataStream[] GetMessageProcessorDiagnosticsStream()
    {
        var streamCountStream = new DataStream
        {
            Id = GetStreamCountStreamId(),
            TypeId = StreamCountTypeId,
            Name = StreamCountStreamName,
        };

        var ioRateStream = new DataStream
        {
            Id = GetIoRateStreamId(),
            TypeId = IoRateTypeId,
            Name = IoRateStreamName,
        };

        // AssetCount and EventWriteCount are OMF 2.0 only concepts, so they are not published for earlier OMF versions.
        if (_omfVersion != OmfVersion.Omf20)
        {
            return new[] { streamCountStream, ioRateStream };
        }

        return new[]
        {
            streamCountStream,
            new DataStream
            {
                Id = GetAssetCountStreamId(),
                TypeId = AssetCountTypeId,
                Name = AssetCountStreamName,
            },
            new DataStream
            {
                Id = GetEventWriteCountStreamId(),
                TypeId = EventWriteCountTypeId,
                Name = EventWriteCountStreamName,
            },
            ioRateStream,
        };
    }

    #endregion
}
