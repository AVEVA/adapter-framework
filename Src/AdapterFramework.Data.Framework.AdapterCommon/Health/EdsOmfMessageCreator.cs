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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;

namespace AdapterFramework.Data.Framework.AdapterCommon.Health;

internal class EdsOmfMessageCreator : AdapterHealthOmfMessageCreatorBase
{
    private readonly string _adapterType;
    private readonly string _baseStreamId;

    public EdsOmfMessageCreator(IApplicationManifest manifest, string adapterType, string version, string componentId, LinkNode parent, string endpointId = null)
        : base(manifest.MachineName, adapterType, version, componentId, parent, endpointId)
    {
        var healthPrefix = manifest.HealthPrefix ?? string.Empty;
        _baseStreamId = $"{healthPrefix}{manifest.MachineName}.{componentId}";
        _adapterType = adapterType;
    }

    protected override string GetDeviceStatusStreamId() => $"{_baseStreamId}.{DeviceStatus}";
    protected override string GetHeartbeatStreamId() => $"{_baseStreamId}.{NextHealthMessageExpected}";
    protected override string GetComponentAssetId() => $"{_baseStreamId}";
    protected override string GetComponentDescription() => $"{EdgeSystemConstants.EdsAdaptersTypePrefix} {_adapterType} {AdapterComponentHealthDescriptionPostFix}";
}
