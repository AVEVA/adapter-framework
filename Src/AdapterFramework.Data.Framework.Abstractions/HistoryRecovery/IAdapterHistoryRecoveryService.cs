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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Services;

namespace AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

public interface IAdapterHistoryRecoveryService : IAdapterCommonService
{
    /// <summary>
    /// Updates checkpoint property in the <see cref="HistoryRecoveryState"/> object to allow user to resume history recovery operation in case of a failure.
    /// </summary>
    /// <param name="dateTime">Timestamp to update the checkpoint property to.</param>
    void UpdateCheckpoint(DateTime dateTime);

    /// <summary>
    /// Updates progress property in the <see cref="DiscoveryState"/> object to inform user about the discovery progress.
    /// </summary>
    /// <param name="historyRecoveryProgress">Number from 0 to a 100.</param>
    void UpdateProgress(int historyRecoveryProgress);
}
