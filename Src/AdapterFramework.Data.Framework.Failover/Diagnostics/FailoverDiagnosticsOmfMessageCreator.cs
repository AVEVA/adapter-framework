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
using System.Collections.Generic;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Failover.Health;

public class FailoverDiagnosticsOmfMessageCreator
{
    private const string FailoverStatusStreamName = "FailoverStatus";

    private readonly string _baseStreamId;
    private readonly LinkNode _parent;

    public FailoverDiagnosticsOmfMessageCreator(IApplicationManifest manifest, LinkNode parent)
    {
        ThrowHelper.ThrowIfArgumentNull(manifest, nameof(manifest));
        ThrowHelper.ThrowIfArgumentNull(parent, nameof(parent));

        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        _baseStreamId = $"{healthPrefix}{manifest.MachineName}.{manifest.ServiceName}.{FailoverConstants.FailoverKeyword}";
        _parent = parent;
    }

    public void CreateAndSendStructure(IDiagnosticsMessageProcessor messageProcessor)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));
        messageProcessor.WriteDiagnosticsTypes(GetTypes());
        messageProcessor.WriteDiagnosticsStreams(GetStreams());

        SendLinks(messageProcessor);
    }

    public void SendLinks(IDiagnosticsMessageProcessor messageProcessor)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));

        foreach (var (id, classification, instance) in GetLinks())
        {
            messageProcessor.WriteDiagnosticsValue(id, classification, instance);
        }
    }

    public string GetFailoverStatusStreamId() => $"{_baseStreamId}.{FailoverStatusStreamName}";

    #region Private Methods

    private static DataType[] GetTypes()
    {
        return new[]
        {
            GetFailoverStatusType(),
        };
    }

    private static DataType GetFailoverStatusType()
    {
        var timestampProperty = new PropertyDefinition
        {
            IsIndex = true,
            Type = Tokens.StringToken,
            Format = Tokens.DateTimeToken,
        };

        var failoverScoreProperty = new PropertyDefinition
        {
            Type = Tokens.NumberToken,
            Format = Tokens.Float64Token,
        };

        var failoverRoleProperty = new PropertyDefinition
        {
            Type = Tokens.StringToken,
        };

        var failoverStatusType = new DynamicDataType
        {
            Id = FailoverStatusStreamName,
            Properties = new Dictionary<string, PropertyDefinition>
            {
                [FailoverConstants.TimestampPropertyName] = timestampProperty,
                [FailoverConstants.FailoverScorePropertyName] = failoverScoreProperty,
                [FailoverConstants.FailoverRolePropertyName] = failoverRoleProperty,
            },
        };

        return failoverStatusType;
    }

    private DataStream[] GetStreams()
    {
        return new[]
        {
            // FailoverStatus stream
            new DataStream()
            {
                Id = GetFailoverStatusStreamId(),
                TypeId = FailoverStatusStreamName,
                Name = FailoverStatusStreamName,
            },
        };
    }

    private List<(string, Classification, object)> GetLinks()
    {
        var links = new List<(string, Classification, object)>();
        var sourceLink = _parent;

        // link dynamic failoverStatus stream to static failover asset type failoverHealth
        var targetLink = new DataStreamLinkNode(GetFailoverStatusStreamId());
        var link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        return links;
    }

    #endregion
}
