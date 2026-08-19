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
using AdapterFramework.Data.Framework.Abstractions.Failover;

namespace AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

public interface IHistoryRecoveryManager : IDisposable
{
    /// <summary>
    /// History recovery processor handling on-demand history recovery operations.
    /// </summary>
    IOnDemandHistoryRecoveryProcessor OnDemandHistoryRecoveryProcessor { get; }

    /// <summary>
    /// History recovery processor for handling automatic history recovery operations.
    /// </summary>
    IAutomaticHistoryRecoveryProcessor AutomaticHistoryRecoveryProcessor { get; }

    /// <summary>
    /// Try processing the data source configuration updates to check the data collection modes and start/stop the history recovery processors accordingly.
    /// </summary>
    /// <param name="dataSourceConfiguration">The data source configuration containing the data collection mode.</param>
    /// <param name="errorMessage">The error message about the data source configuration processing.</param>
    /// <returns>Whether the data source configuration is processed or not.</returns>
    bool TryProcessDataSourceUpdate(IHistoryDataSourceConfiguration dataSourceConfiguration, out string errorMessage);

    /// <summary>
    /// Process the data selection item updates to push the changes through to the history recovery processors.
    /// </summary>
    /// <param name="dataSelectionItems">The data selection items to update the history recovery processors.</param>
    void ProcessDataSelectionUpdate(IDataSelectionConfiguration[] dataSelectionItems);

    /// <summary>
    /// Returns true when on-demand history recovery is in progress.
    /// </summary>
    /// <returns>True when on-demand history recovery operation is in progress; False otherwise.</returns>
    bool IsOnDemandRecoveryInProgress();

    /// <summary>
    /// Update the failover mode.
    /// </summary>
    /// <param name="failoverMode">The new failover mode to update.</param>
    void UpdateFailoverMode(FailoverMode failoverMode);

    /// <summary>
    /// Update the failover state.
    /// </summary>
    /// <param name="failoverRole">The new failover state to update.</param>
    void UpdateFailoverRole(FailoverRole failoverRole);
}
