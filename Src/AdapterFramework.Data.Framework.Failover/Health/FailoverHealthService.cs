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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.Health;
using AdapterFramework.Data.Framework.Common.HierarchyCreation;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Failover.Configuration;

namespace AdapterFramework.Data.Framework.Failover.Health;

public class FailoverHealthService : HealthServiceBase
{
    private readonly IHealthMessageProcessor _healthMessageProcessor;
    private ClientFailoverConfiguration _clientFailoverConfiguration;

    public FailoverHealthService(
        IHealthMessageProcessor healthMessageProcessor,
        ILogger logger,
        IApplicationManifest applicationManifest,
        ClientFailoverConfiguration configuration)
        : base(healthMessageProcessor, logger, EdgeSystemConstants.FailoverComponentType, EdgeSystemConstants.FailoverComponentType)
    {
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        var parentNode = AdapterBaseHierarchyCreator.GetServiceNode(
            applicationManifest.HealthPrefix,
            applicationManifest.MachineName,
            applicationManifest.ServiceName);

        HealthMessageCreator = new FailoverHealthOmfMessageCreator(applicationManifest, configuration, parentNode);

        _healthMessageProcessor = healthMessageProcessor;
        _clientFailoverConfiguration = configuration;
    }

    protected override FailoverHealthOmfMessageCreator HealthMessageCreator { get; }

    public void SendClientConfiguration(ClientFailoverConfiguration configuration)
    {
        ThrowHelper.ThrowIfArgumentNull(configuration, nameof(configuration));
        ThrowHelper.ThrowIfArgumentNull(configuration.FailoverGroupId, nameof(configuration.FailoverGroupId));
        ThrowHelper.ThrowIfArgumentNull(configuration.Mode, nameof(configuration.Mode));

        if (_clientFailoverConfiguration.Endpoint != configuration.Endpoint ||
            _clientFailoverConfiguration.Mode != configuration.Mode ||
            _clientFailoverConfiguration.FailoverGroupId != configuration.FailoverGroupId)
        {
            SendClientConfigurationInternal(configuration);
        }
    }

    public void ResetFailoverAsset()
    {
        var (assetId, classification, instance) = HealthMessageCreator.ResetComponentAsset();
        _healthMessageProcessor?.WriteHealthValue(assetId, classification, instance);
        _clientFailoverConfiguration = null;
    }

    private void SendClientConfigurationInternal(ClientFailoverConfiguration configuration)
    {
        var (assetId, classification, instance) = HealthMessageCreator.GetComponentAsset(configuration);
        _healthMessageProcessor?.WriteHealthValue(assetId, classification, instance);
        _clientFailoverConfiguration = configuration;
    }
}
