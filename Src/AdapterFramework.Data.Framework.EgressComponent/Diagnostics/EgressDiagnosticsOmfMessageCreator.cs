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
using System.Globalization;
using System.Runtime.CompilerServices;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Common.Diagnostics.Events;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Common.Constants.DiagnosticsConstants;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.EgressComponent.Tests")]

namespace AdapterFramework.Data.Framework.EgressComponent.Diagnostics;

internal class EgressDiagnosticsOmfMessageCreator
{
    private readonly string _egressStreamIdPrefix;
    private readonly LinkNode _elementNode;

    /// <summary>
    /// Initializes a new instance of the <see cref="EgressDiagnosticsOmfMessageCreator"/> class.
    /// </summary>
    /// <param name="componentId"> The component id.</param>
    /// <param name="elementNode">The element node to link all the streams to.</param>
    /// <param name="streamIdPrefix">The stream id prefix.</param>
    public EgressDiagnosticsOmfMessageCreator(string componentId, LinkNode elementNode, string streamIdPrefix = null)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNull(elementNode, nameof(elementNode));

        _elementNode = elementNode;
        _egressStreamIdPrefix = $"{streamIdPrefix}{componentId}.";
    }

    public static DataType[] GetTypes() => new[] { CreateIoRateType(), };

    public DataStream CreateIoRateStream(string endpointId) => CreateIoRateStream(endpointId, IoRateStreamName);

    /// <summary>
    /// Creates an IO rate stream for an endpoint, such as <c>{StreamIdPrefix}{ComponentId}.{EndpointId}.StreamIORate</c>.
    /// All IO rate streams share the <see cref="IoRateTypeId"/> type.
    /// </summary>
    /// <param name="endpointId">The egress endpoint id.</param>
    /// <param name="streamName">The IO rate stream name, for example <see cref="StreamIoRateStreamName"/>.</param>
    /// <returns>The IO rate stream.</returns>
    public DataStream CreateIoRateStream(string endpointId, string streamName) =>
        new DataStream(IoRateTypeId, string.Create(CultureInfo.InvariantCulture, $"{_egressStreamIdPrefix}{endpointId}.{streamName}"), $"{endpointId}.{streamName}");

    public ValueTuple<string, Classification, object> CreateLink(string streamId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(streamId, nameof(streamId));

        var sourceLink = _elementNode;
        LinkNode targetLink = new DataStreamLinkNode(streamId);

        var link = new Link(sourceLink, targetLink);

        return (Tokens.Link, Classification.Static, link);
    }

    private static DataType CreateIoRateType()
    {
        var properties = new Dictionary<string, (Type PropertyType, bool IsIndex, bool IsQuality, string QualitySchema)>
        {
            [nameof(IoRateEvent.IORate)] = (typeof(double), false, false, null),
            [nameof(IoRateEvent.Timestamp)] = (typeof(DateTime), true, false, null),
        };

        return new DynamicDataType(IoRateTypeId, IoRateTypeId, properties);
    }
}
