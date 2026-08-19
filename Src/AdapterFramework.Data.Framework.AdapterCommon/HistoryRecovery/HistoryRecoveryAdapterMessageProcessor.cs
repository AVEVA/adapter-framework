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
using System.Collections.Generic;
using System.Threading;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.MessageProcessor;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class HistoryRecoveryAdapterMessageProcessor : AdapterMessageProcessor
{    
    private Action<long> _eventCountUpdateAction;
    private long _eventsCount;

    public HistoryRecoveryAdapterMessageProcessor(IInstrumentedMessageProcessor messageProcessor, OmfVersion omfVersion = OmfVersion.Omf12) : base(messageProcessor, omfVersion)
    {
    }

    /// <summary>
    /// Sets action to update recovered event count.
    /// </summary>
    /// <param name="eventCountUpdateAction">Event count update action.</param>
    public void SetEventCountUpdateAction(Action<long> eventCountUpdateAction)
    {
        _eventCountUpdateAction = eventCountUpdateAction;
    }

    /// <inheritdoc/>
    public override void WriteDynamicValue<T>(IDataSelectionConfiguration dataSelectionItem, T instance, MessageAction messageAction, PartitionKey? partitionKey = null)
    {
        ThrowIfInvalidPartitionKey(partitionKey);

        _eventCountUpdateAction?.Invoke(Interlocked.Increment(ref _eventsCount));
        base.WriteDynamicValue(dataSelectionItem, instance, messageAction, partitionKey);
    }

    /// <inheritdoc/>
    public override void WriteDynamicValues<T>(IDataSelectionConfiguration dataSelectionItem, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey = null)
    {
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));
        ThrowIfInvalidPartitionKey(partitionKey);

        _eventCountUpdateAction?.Invoke(Interlocked.Add(ref _eventsCount, instances.Count));
        base.WriteDynamicValues(dataSelectionItem, instances, messageAction, partitionKey);
    }
}
