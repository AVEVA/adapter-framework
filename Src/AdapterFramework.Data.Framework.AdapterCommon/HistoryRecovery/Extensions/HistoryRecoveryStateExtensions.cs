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
using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery.Extensions;

public static class HistoryRecoveryStateExtensions
{
    private const string InvalidHistoryRecoveryStatus = "Cannot set HistoryRecoveryStatus because it has been previously set.";

    public static HistoryRecoveryState OnStarted(this HistoryRecoveryState historyRecoveryState, DateTime startTime, DateTime endTime, int items)
    {
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryState, nameof(historyRecoveryState));

        historyRecoveryState.StartTime = startTime;
        historyRecoveryState.EndTime = endTime;
        historyRecoveryState.Checkpoint = null;
        historyRecoveryState.Progress = 0;
        historyRecoveryState.Items = items;
        historyRecoveryState.RecoveredEvents = 0;
        historyRecoveryState.Status = OperationStatus.Active;
        historyRecoveryState.Errors = null;

        return historyRecoveryState;
    }

    public static HistoryRecoveryState OnCompleted(this HistoryRecoveryState historyRecoveryState)
    {
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryState, nameof(historyRecoveryState));

        if (!historyRecoveryState.Status.Equals(OperationStatus.Active))
        {
            throw new InvalidOperationException(InvalidHistoryRecoveryStatus);
        }

        historyRecoveryState.Progress = 100;
        historyRecoveryState.Status = OperationStatus.Complete;

        return historyRecoveryState;
    }

    public static HistoryRecoveryState OnFailed(this HistoryRecoveryState historyRecoveryState, string errors)
    {
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryState, nameof(historyRecoveryState));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(errors, nameof(errors));

        if (!historyRecoveryState.Status.Equals(OperationStatus.Active))
        {
            throw new InvalidOperationException(InvalidHistoryRecoveryStatus);
        }

        historyRecoveryState.Status = OperationStatus.Failed;
        historyRecoveryState.Errors = errors;

        return historyRecoveryState;
    }

    public static HistoryRecoveryState OnCanceled(this HistoryRecoveryState historyRecoveryState, string errors)
    {
        ThrowHelper.ThrowIfArgumentNull(historyRecoveryState, nameof(historyRecoveryState));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(errors, nameof(errors));

        if (!historyRecoveryState.Status.Equals(OperationStatus.Active))
        {
            throw new InvalidOperationException(InvalidHistoryRecoveryStatus);
        }

        historyRecoveryState.Status = OperationStatus.Canceled;
        historyRecoveryState.Errors = errors;

        return historyRecoveryState;
    }
}
