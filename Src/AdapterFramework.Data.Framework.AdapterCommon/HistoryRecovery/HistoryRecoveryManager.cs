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
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class HistoryRecoveryManager<TDataSource, TSelection> : IHistoryRecoveryManager
    where TDataSource : class, IDataSourceConfiguration
    where TSelection : class, IDataSelectionConfiguration
{
    private readonly object _lockObject = new object();
    private bool _disposed;

    public HistoryRecoveryManager(IOnDemandHistoryRecoveryProcessor onDemandHistoryRecoveryProcessor, IAutomaticHistoryRecoveryProcessor automaticHistoryRecoveryProcessor)
    {
        ThrowHelper.ThrowIfArgumentNull(onDemandHistoryRecoveryProcessor, nameof(onDemandHistoryRecoveryProcessor));
        ThrowHelper.ThrowIfArgumentNull(automaticHistoryRecoveryProcessor, nameof(automaticHistoryRecoveryProcessor));

        OnDemandHistoryRecoveryProcessor = onDemandHistoryRecoveryProcessor;
        AutomaticHistoryRecoveryProcessor = automaticHistoryRecoveryProcessor;
    }

    public IOnDemandHistoryRecoveryProcessor OnDemandHistoryRecoveryProcessor { get; }

    public IAutomaticHistoryRecoveryProcessor AutomaticHistoryRecoveryProcessor { get; }

    public bool TryProcessDataSourceUpdate(IHistoryDataSourceConfiguration dataSourceConfiguration, out string errorMessage)
    {
        lock (_lockObject)
        {
            if (dataSourceConfiguration == null || dataSourceConfiguration.DataCollectionMode == DataCollectionMode.CurrentOnly)
            {
                if (IsOnDemandRecoveryInProgress())
                {
                    errorMessage = $"Cannot stop on-demand history recovery operation since the operation with ID {OnDemandHistoryRecoveryProcessor.ActiveRecoveryId} is still in progress. Delete the operation before proceeding. ";
                    return false;
                }

                if (OnDemandHistoryRecoveryProcessor.Started)
                {
                    OnDemandHistoryRecoveryProcessor.Stop();
                }

                if (AutomaticHistoryRecoveryProcessor.Started)
                {
                    AutomaticHistoryRecoveryProcessor.Stop();
                }
            }
            else if (dataSourceConfiguration.DataCollectionMode == DataCollectionMode.HistoryOnly)
            {
                if (AutomaticHistoryRecoveryProcessor.Started)
                {
                    AutomaticHistoryRecoveryProcessor.Stop();
                }

                if (!OnDemandHistoryRecoveryProcessor.Started)
                {
                    OnDemandHistoryRecoveryProcessor.Start(dataSourceConfiguration);
                }
                else
                {
                    OnDemandHistoryRecoveryProcessor.UpdateDataSourceConfiguration(dataSourceConfiguration);
                }
            }
            else if (dataSourceConfiguration.DataCollectionMode == DataCollectionMode.CurrentWithBackfill)
            {
                if (OnDemandHistoryRecoveryProcessor.Started)
                {
                    if (OnDemandHistoryRecoveryProcessor.ActiveRecoveryId != null)
                    {
                        errorMessage = $"Cannot stop on-demand history recovery operation since the operation with ID {OnDemandHistoryRecoveryProcessor.ActiveRecoveryId} is still in progress. Delete the operation before proceeding. ";
                        return false;
                    }

                    OnDemandHistoryRecoveryProcessor.Stop();
                }

                if (!AutomaticHistoryRecoveryProcessor.Started)
                {
                    AutomaticHistoryRecoveryProcessor.Start(dataSourceConfiguration);
                }
                else
                {
                    AutomaticHistoryRecoveryProcessor.UpdateDataSourceConfiguration(dataSourceConfiguration);
                }
            }
        }

        errorMessage = null;
        return true;
    }

    public bool IsOnDemandRecoveryInProgress()
    {
        lock (_lockObject)
        {
            if (OnDemandHistoryRecoveryProcessor.Started)
            {
                if (OnDemandHistoryRecoveryProcessor.ActiveRecoveryId != null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void ProcessDataSelectionUpdate(IDataSelectionConfiguration[] dataSelectionItems)
    {
        lock (_lockObject)
        {
            OnDemandHistoryRecoveryProcessor.UpdateDataSelectionItems(dataSelectionItems);
            AutomaticHistoryRecoveryProcessor.UpdateDataSelectionItems(dataSelectionItems);
        }
    }

    public void UpdateFailoverMode(FailoverMode failoverMode)
    {
        AutomaticHistoryRecoveryProcessor.UpdateFailoverMode(failoverMode);
    }

    public void UpdateFailoverRole(FailoverRole failoverRole)
    {
        AutomaticHistoryRecoveryProcessor.UpdateFailoverRole(failoverRole);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        OnDemandHistoryRecoveryProcessor.Dispose();
        AutomaticHistoryRecoveryProcessor.Dispose();
        _disposed = true;
    }
}
