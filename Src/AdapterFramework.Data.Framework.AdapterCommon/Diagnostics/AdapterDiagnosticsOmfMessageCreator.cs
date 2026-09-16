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

    #endregion

    #region Constructors

    public AdapterDiagnosticsOmfMessageCreator(string componentId, string streamIdPrefix, LinkNode assetNode)
    {
        ThrowHelper.ThrowIfArgumentNull(assetNode, nameof(assetNode));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        _streamIdPrefix = streamIdPrefix != null 
            ? $"{streamIdPrefix}{componentId}" 
            : componentId;

        _assetNode = assetNode;
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

    public string GetErrorRateStreamId() => $"{_streamIdPrefix}.{ErrorRateStreamName}";

    private static DataType[] GetTypes()
    {
        var errorRateType = new[] { GetErrorRateType() };
        var otherTypes = GetMessageProcessorDiagnosticsTypes();
        return [.. errorRateType.Union(otherTypes)];
    }

    private static DataType[] GetMessageProcessorDiagnosticsTypes()
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

        var dataRateDiagnosticsType = new DynamicDataType
        {
            Id = IoRateTypeId,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [nameof(IoRateEvent.Timestamp)] = timestampProperty,
                [nameof(IoRateEvent.IORate)] = doubleProperty,
            },
        };

        return new DataType[] { streamCountDiagnosticsType, assetCountDiagnosticsType, dataRateDiagnosticsType };
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

        // Link adapter asset count to adapter component health asset
        targetLink = new DataStreamLinkNode(GetAssetCountStreamId());
        link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

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
        return new[]
        {
            new DataStream
            {
                Id = GetStreamCountStreamId(),
                TypeId = StreamCountTypeId,
                Name = StreamCountStreamName,
            },
            new DataStream
            {
                Id = GetAssetCountStreamId(),
                TypeId = AssetCountTypeId,
                Name = AssetCountStreamName,
            },
            new DataStream
            {
                Id = GetIoRateStreamId(),
                TypeId = IoRateTypeId,
                Name = IoRateStreamName,
            },
        };
    }

    #endregion
}
