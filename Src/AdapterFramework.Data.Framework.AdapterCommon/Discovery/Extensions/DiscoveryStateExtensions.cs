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

namespace AdapterFramework.Data.Framework.AdapterCommon.Discovery.Extensions;

public static class DiscoveryStateExtensions
{
    private const string InvalidDiscoveryStatus = "Cannot set DiscoverStatus because it has been previously set.";
    private const string InvalidEndTime = "EndTime cannot be earlier than StartTime.";
    private const string InvalidNegativeCount = "Must be equal to or greater than zero.";
    private const string NewItemsGreaterThanTotalItems = "The total number of items found must be equal to or greater than the number of new items found.";

    public static DiscoveryState OnStarted(this DiscoveryState discoveryState, DateTime startTime)
    {
        ThrowHelper.ThrowIfArgumentNull(discoveryState, nameof(discoveryState));

        discoveryState.StartTime = startTime;
        discoveryState.Status = OperationStatus.Active;

        return discoveryState;
    }

    public static DiscoveryState OnCompleted(this DiscoveryState discoveryState, DateTime endTime, int itemsFound, int newItems, string unifiedRecordLocator)
    {
        ThrowHelper.ThrowIfArgumentNull(discoveryState, nameof(discoveryState));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(unifiedRecordLocator, nameof(unifiedRecordLocator));

        if (!discoveryState.Status.Equals(OperationStatus.Active))
        {
            throw new InvalidOperationException(InvalidDiscoveryStatus);
        }

        if (endTime < discoveryState.StartTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), InvalidEndTime);
        }

        if (itemsFound < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemsFound), InvalidNegativeCount);
        }

        if (newItems < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newItems), InvalidNegativeCount);
        }

        if (newItems > itemsFound)
        {
            throw new ArgumentOutOfRangeException(nameof(newItems), NewItemsGreaterThanTotalItems);
        }

        discoveryState.EndTime = endTime;
        discoveryState.ItemsFound = itemsFound;
        discoveryState.NewItems = newItems;
        discoveryState.ResultUri = unifiedRecordLocator;
        discoveryState.Status = OperationStatus.Complete;

        return discoveryState;
    }

    public static DiscoveryState OnFailed(this DiscoveryState discoveryState, DateTime endTime, string errors)
    {
        ThrowHelper.ThrowIfArgumentNull(discoveryState, nameof(discoveryState));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(errors, nameof(errors));

        if (!discoveryState.Status.Equals(OperationStatus.Active))
        {
            throw new InvalidOperationException(InvalidDiscoveryStatus);
        }

        if (endTime < discoveryState.StartTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), InvalidEndTime);
        }

        discoveryState.EndTime = endTime;
        discoveryState.Status = OperationStatus.Failed;
        discoveryState.Errors = errors;

        return discoveryState;
    }

    public static DiscoveryState OnCanceled(this DiscoveryState discoveryState, DateTime endTime, string errors)
    {
        ThrowHelper.ThrowIfArgumentNull(discoveryState, nameof(discoveryState));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(errors, nameof(errors));

        if (!discoveryState.Status.Equals(OperationStatus.Active))
        {
            throw new InvalidOperationException(InvalidDiscoveryStatus);
        }

        if (endTime < discoveryState.StartTime)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), InvalidEndTime);
        }

        discoveryState.EndTime = endTime;
        discoveryState.Status = OperationStatus.Canceled;
        discoveryState.Errors = errors;

        return discoveryState;
    }
}
