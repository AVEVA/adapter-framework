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
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.HierarchyCreation;
using static AdapterFramework.Data.Framework.Common.Constants.DiagnosticsConstants;

namespace AdapterFramework.Data.Framework.Diagnostics;

internal class DiagnosticsOmfMessageCreator
{
    internal static IEnumerable<ValueTuple<string, Classification, object>> GetLinks(IApplicationManifest manifest, bool hasStorageComponent)
    {
        var links = new List<(string, Classification, object)>();

        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        var adapterServiceAssetId = $"{healthPrefix}{manifest.MachineName}.{manifest.ServiceName}";
        var adapterSystemDiagnosticsId = $"{adapterServiceAssetId}.{SystemDiagnosticsStreamId}";

        var sourceLink = hasStorageComponent
            ? EdsBaseHierarchyCreator.GetServiceNode(healthPrefix, manifest.MachineName, manifest.ServiceName)
            : AdapterBaseHierarchyCreator.GetServiceNode(healthPrefix, manifest.MachineName, manifest.ServiceName);

        LinkNode targetLink = new DataStreamLinkNode(adapterSystemDiagnosticsId);
        var link = new Link(sourceLink, targetLink);
        links.Add((Tokens.Link, Classification.Static, link));

        return links;
    }

    internal static DataType GetSystemDiagnosticsDataType()
    {
        var systemDiagnosticsType = new DynamicDataType
        {
            Id = SystemDiagnosticsTypeId,
        };
        var timestampIdProperty = new PropertyDefinition
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
        var startTime = new PropertyDefinition
        {
            Type = Tokens.StringToken,
            Format = Tokens.DateTimeToken,
        };
        var processorTimeProperty = new PropertyDefinition
        {
            Type = Tokens.NumberToken,
            Format = Tokens.Float64Token,
            Uom = "s",
        };
        var memorySizeProperty = new PropertyDefinition
        {
            Type = Tokens.NumberToken,
            Format = Tokens.Float64Token,
            Uom = "MB",
        };

        systemDiagnosticsType.Properties = new Dictionary<string, PropertyDefinition>
        {
            [nameof(EdgeDiagnosticsEvent.Timestamp)] = timestampIdProperty,
            [nameof(EdgeDiagnosticsEvent.ProcessIdentifier)] = integerProperty,
            [nameof(EdgeDiagnosticsEvent.StartTime)] = startTime,
            [nameof(EdgeDiagnosticsEvent.WorkingSet)] = memorySizeProperty,
            [nameof(EdgeDiagnosticsEvent.TotalProcessorTime)] = processorTimeProperty,
            [nameof(EdgeDiagnosticsEvent.TotalUserProcessorTime)] = processorTimeProperty,
            [nameof(EdgeDiagnosticsEvent.TotalPrivilegedProcessorTime)] = processorTimeProperty,
            [nameof(EdgeDiagnosticsEvent.ThreadCount)] = integerProperty,
            [nameof(EdgeDiagnosticsEvent.HandleCount)] = integerProperty,
            [nameof(EdgeDiagnosticsEvent.ManagedMemorySize)] = memorySizeProperty,
            [nameof(EdgeDiagnosticsEvent.PrivateMemorySize)] = memorySizeProperty,
            [nameof(EdgeDiagnosticsEvent.PeakPagedMemorySize)] = memorySizeProperty,
            [nameof(EdgeDiagnosticsEvent.StorageTotalSize)] = memorySizeProperty,
            [nameof(EdgeDiagnosticsEvent.StorageFreeSpace)] = memorySizeProperty,
        };

        return systemDiagnosticsType;
    }

    internal static string GetSystemDiagnosticsDataStreamId(IApplicationManifest manifest)
    {
        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        var adapterServiceAssetId = $"{healthPrefix}{manifest.MachineName}.{manifest.ServiceName}";
        return $"{adapterServiceAssetId}.{SystemDiagnosticsStreamId}";
    }

    internal static DataStream GetSystemDiagnosticsDataStream(IApplicationManifest manifest)
    {
        var adapterSystemDiagnosticsId = GetSystemDiagnosticsDataStreamId(manifest);

        return new DataStream
        {
            Id = adapterSystemDiagnosticsId,
            TypeId = SystemDiagnosticsTypeId,
            Name = SystemDiagnosticsStreamName,
        };
    }
}
