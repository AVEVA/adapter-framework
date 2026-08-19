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
using System.Collections.Concurrent;
using System.Linq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.EgressComponent;

public class DiagnosticsMessageProcessor : IDiagnosticsMessageProcessor
{
    #region Private Fields

    private readonly ConcurrentDictionary<string, DataType> _diagnosticsTypes = new();
    private readonly ConcurrentDictionary<string, DataStream> _diagnosticsStreams = new();
    private readonly ConcurrentDictionary<string, Link> _diagnosticsLinks = new();
    private readonly IHealthMessageProcessor _healthMessageProcessor;

    private bool _diagnosticsEnabled;
    private MetadataInfo _streamMetadataEnabled;

    #endregion

    #region Public Constructor

    public DiagnosticsMessageProcessor(IConfigurationProvider configurationProvider, IHealthMessageProcessor healthMessageProcessor,
        IApplicationManifest applicationManifest)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        _healthMessageProcessor = healthMessageProcessor;
        var healthPrefix = applicationManifest.HealthPrefix ?? string.Empty;
        StreamIdPrefix = $"{healthPrefix}{applicationManifest.MachineName}.{applicationManifest.ServiceName}.";

        if (!configurationProvider.TryGetConfiguration<GeneralConfiguration>(EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.GeneralFacetName, out var generalConfiguration, out _))
        {
            _diagnosticsEnabled = true;
            return;
        }

        _diagnosticsEnabled = generalConfiguration.EnableDiagnostics;
    }

    #endregion

    #region Public Properties

    public string StreamIdPrefix { get; }

    public MetadataInfo StreamMetadataLevel
    {
        get => _streamMetadataEnabled; 
        set 
        { 
            _healthMessageProcessor.StreamMetadataLevel = value; 
            _streamMetadataEnabled = value;
        }
    }

    public bool SystemDiagnosticsEnabled
    {
        get => _diagnosticsEnabled;
        set
        {
            if (_diagnosticsEnabled == value)
            {
                return;
            }

            _diagnosticsEnabled = value;

            if (value)
            {
                WriteDiagnosticsTypes([.. _diagnosticsTypes.Values]);
                WriteDiagnosticsStreams([.. _diagnosticsStreams.Values]);

                foreach (var link in _diagnosticsLinks.Values)
                {
                    WriteDiagnosticsValue(Tokens.Link, Classification.Static, link);
                }
            }
        }
    }

    #endregion

    #region Public Methods

    public void WriteDiagnosticsTypes(DataType[] dataTypes)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));

        foreach (var dataType in dataTypes)
        {
            _diagnosticsTypes.AddOrUpdate(dataType.Id, dataType, (key, value) => dataType);
        }

        if (_diagnosticsEnabled)
        {
            _healthMessageProcessor.WriteHealthTypes(dataTypes);
        }
    }

    public void WriteDiagnosticsStreams(DataStream[] dataStreams)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));

        foreach (var dataStream in dataStreams)
        {
            _diagnosticsStreams.AddOrUpdate(dataStream.Id, dataStream, (key, value) => dataStream);
        }

        if (_diagnosticsEnabled)
        {
            _healthMessageProcessor.WriteHealthStreams(dataStreams);
        }
    }

    public void WriteDiagnosticsValue<T>(string id, Classification classification, T instance)
    {
        if (classification == Classification.Static)
        {
            if (id == Tokens.Link && instance is Link link)
            {
                _diagnosticsLinks[link.Target.Id] = link;
            }
        }

        if (_diagnosticsEnabled)
        {
            _healthMessageProcessor.WriteHealthValue(id, classification, instance);
        }
    }

    #endregion
}
