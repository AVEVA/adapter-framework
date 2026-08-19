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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class AdapterHistoryRecoveryService : AdapterCommonService, IAdapterHistoryRecoveryService
{
    private readonly HistoryRecoveryState _historyRecoveryState;

    public AdapterHistoryRecoveryService(HistoryRecoveryState historyRecoveryState, ILogger logger, IConfigurationProvider configurationProvider, IAdapterMessageProcessor messageProcessor, IEdgeDataProtector edgeDataProtector, string adapterType, string adapterId, IHealthService healthService) : base(logger, configurationProvider, messageProcessor, edgeDataProtector, adapterType, adapterId, healthService)
    {
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryState, nameof(historyRecoveryState));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));
        ThrowHelper.ThrowIfArgumentNull(edgeDataProtector, nameof(edgeDataProtector));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(adapterType, nameof(adapterType));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(adapterId, nameof(adapterId));
        ThrowHelper.ThrowIfArgumentNull(healthService, nameof(healthService));

        _historyRecoveryState = historyRecoveryState;
    }

    public void UpdateCheckpoint(DateTime dateTime)
    {
        _historyRecoveryState.Checkpoint = dateTime;
    }

    public void UpdateProgress(int historyRecoveryProgress)
    {
        _historyRecoveryState.Progress = historyRecoveryProgress;
    }

    public void UpdateRecoveredEvents(long eventCount)
    {
        _historyRecoveryState.RecoveredEvents = eventCount;
    }
}
