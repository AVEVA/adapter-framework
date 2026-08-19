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
using System.Linq;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public abstract class HistoryRecoveryProcessorBase<TDataSource, TSelection> : IHistoryRecoveryProcessorBase
    where TDataSource : class, IDataSourceConfiguration
    where TSelection : class, IDataSelectionConfiguration
{
    protected HistoryRecoveryProcessorBase(string componentId, ILogger logger)
    {
        ComponentId = componentId;
        Logger = logger;
    }

    public bool Started { get; protected set; }

    protected string ComponentId { get; }

    protected ILogger Logger { get; }

    protected TDataSource DataSourceConfiguration { get; private set; }

    protected TSelection[] DataSelectionItems { get; private set; }

    public abstract void Start(IDataSourceConfiguration dataSourceConfiguration);

    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Lifecycle API uses Start/Stop naming for consistency across adapters.")]
    public abstract void Stop();

    public void UpdateDataSourceConfiguration(IDataSourceConfiguration dataSourceConfiguration)
    {
        if (dataSourceConfiguration is TDataSource typedDataSourceConfiguration)
        {
            DataSourceConfiguration = typedDataSourceConfiguration;
        }
    }

    public void UpdateDataSelectionItems(IDataSelectionConfiguration[] dataSelectionItems)
    {
        if (dataSelectionItems is TSelection[] typedDataSelectionItems)
        {
            DataSelectionItems = [.. typedDataSelectionItems.Where(d => d.Selected)];
        }
    }
}
