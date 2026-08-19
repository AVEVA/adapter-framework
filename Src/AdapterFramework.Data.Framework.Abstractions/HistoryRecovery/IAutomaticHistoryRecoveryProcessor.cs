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
using AdapterFramework.Data.Framework.Abstractions.Failover;

namespace AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

public interface IAutomaticHistoryRecoveryProcessor : IHistoryRecoveryProcessorBase, IDisposable
{
    /// <summary>
    /// Handles changes to IntervalsToRecover configuration.
    /// </summary>
    /// <param name="intervals">Current <see cref="Interval"/> array configuration.</param>
    void ProcessIntervalsToRecoverChanges(Interval[] intervals);

    /// <summary>
    /// Update the failover mode.
    /// </summary>
    /// <param name="failoverMode">The new failover mode to update.</param>
    void UpdateFailoverMode(FailoverMode failoverMode);

    /// <summary>
    /// Update the failover role.
    /// </summary>
    /// <param name="failoverRole">The new failover role to update.</param>
    void UpdateFailoverRole(FailoverRole failoverRole);
}
