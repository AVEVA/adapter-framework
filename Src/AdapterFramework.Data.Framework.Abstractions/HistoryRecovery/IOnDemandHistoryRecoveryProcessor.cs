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
using AdapterFramework.Data.Framework.Abstractions.Configuration.Commands;

namespace AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

public interface IOnDemandHistoryRecoveryProcessor : IHistoryRecoveryProcessorBase, IDisposable
{
    /// <summary>
    /// The ID of the history recovery operation in progress.
    /// </summary>
    string ActiveRecoveryId { get; }
    
    /// <summary>
    /// Returns entire <see cref="HistoryRecoveryState"/> configuration.
    /// </summary>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult GetHistoryRecoveryStates();

    /// <summary>
    /// Returns <see cref="HistoryRecoveryState"/> configuration by <paramref name="historyRecoveryId"/>.
    /// </summary>
    /// <param name="historyRecoveryId">Id of history recovery operation to return the <see cref="HistoryRecoveryState"/> for.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult GetHistoryRecoveryState(string historyRecoveryId);

    /// <summary>
    /// Starts a new on-demand history recovery operation.
    /// </summary>
    /// <param name="historyRecoveryState"><see cref="HistoryRecoveryState"/> object containing information about the history recovery.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult StartOnDemandHistoryRecovery(HistoryRecoveryState historyRecoveryState);

    /// <summary>
    /// Cancels history recovery status for the given <paramref name="historyRecoveryId"/>.
    /// </summary>
    /// <param name="historyRecoveryId">Id of the history recovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult CancelHistoryRecovery(string historyRecoveryId);

    /// <summary>
    /// Resumes the canceled or failed history recovery operation from the checkpoint stored in the history recovery state for the given <paramref name="historyRecoveryId"/>.
    /// </summary>
    /// <param name="historyRecoveryId">Id of the history recovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult ResumeHistoryRecovery(string historyRecoveryId);

    /// <summary>
    /// Deletes history recovery status for the given <paramref name="historyRecoveryId"/> and cancels the on-demand history recovery if in progress.
    /// </summary>
    /// <param name="historyRecoveryId">Id of the history recovery operation.</param>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult DeleteHistoryRecovery(string historyRecoveryId);

    /// <summary>
    /// Deletes entire history recovery status collection. On-demand history recovery in progress is going to be canceled and deleted.
    /// </summary>
    /// <returns><see cref="MvcResult"/> representing status of the operation.</returns>
    MvcResult DeleteHistoryRecoveries();
}
