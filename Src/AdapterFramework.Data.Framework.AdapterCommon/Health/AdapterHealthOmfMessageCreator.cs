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
using AdapterFramework.Data.Framework.Abstractions.Services;

namespace AdapterFramework.Data.Framework.AdapterCommon.Health;

internal class AdapterHealthOmfMessageCreator : AdapterHealthOmfMessageCreatorBase
{
    private readonly string _productName;
    private readonly string _baseStreamId;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdapterHealthOmfMessageCreator"/> class.
    /// </summary>
    /// <param name="applicationManifest">The application manifest</param>
    /// <param name="productName">The full name of the adapter.</param>
    /// <param name="version">The adapter version.</param>
    /// <param name="componentId">The adapter instance.</param>
    /// <param name="parent">The parent node to link the created health node to.</param>
    /// <param name="egressEndpoints">Semicolon-separated list of egress endpoints this adapter sends data to.</param>
    public AdapterHealthOmfMessageCreator(IApplicationManifest applicationManifest, string productName, string version, string componentId, LinkNode parent, string egressEndpoints = null)
        : base(applicationManifest.MachineName, productName, version, componentId, parent, egressEndpoints)
    {
        var healthPrefix = applicationManifest.HealthPrefix ?? string.Empty;
        _baseStreamId = $"{healthPrefix}{applicationManifest.MachineName}.{applicationManifest.ServiceName}.{componentId}";
        _productName = productName;
    }

    protected override string GetDeviceStatusStreamId() => $"{_baseStreamId}.{DeviceStatus}";

    protected override string GetHeartbeatStreamId() => $"{_baseStreamId}.{NextHealthMessageExpected}";

    protected override string GetComponentAssetId() => $"{_baseStreamId}";

    protected override string GetComponentDescription() => $"{_productName} {AdapterComponentHealthDescriptionPostFix}";
}
