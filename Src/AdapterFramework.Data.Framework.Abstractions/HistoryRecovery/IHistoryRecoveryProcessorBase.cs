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
using System.Diagnostics.CodeAnalysis;
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Public interface name retained for backward compatibility.")]
public interface IHistoryRecoveryProcessorBase
{
    /// <summary>
    /// Indicates whether the history recovery processor is started or not.
    /// </summary>
    bool Started { get; }

    /// <summary>
    /// Start the history recovery processor on the data source provided.
    /// </summary>
    /// <param name="dataSourceConfiguration">The data source configuration to start history recovery.</param>
    void Start(IDataSourceConfiguration dataSourceConfiguration);

    /// <summary>
    /// Stop the history recovery processor.
    /// </summary>
    void Stop();

    /// <summary>
    /// Update the data source configuration when the history recovery processor is started.
    /// </summary>
    /// <param name="dataSourceConfiguration">The data source configuration to update to the processor.</param>
    void UpdateDataSourceConfiguration(IDataSourceConfiguration dataSourceConfiguration);

    /// <summary>
    /// Update the data selection item when the history recovery processor is started.
    /// </summary>
    /// <param name="dataSelectionItems">The data selection items to set.</param>
    void UpdateDataSelectionItems(IDataSelectionConfiguration[] dataSelectionItems);
}
