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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// Data Flow Block for processing <see cref="GroupedDataMessage"/> instances.
/// </summary>
public class OmfDataMessageBlock : BaseBlock<Message>
{
    private readonly Action<Message> _flush;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmfDataMessageBlock"/> class.
    /// </summary>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity">The max number of messages that may be buffered by the block. Use <see cref="DataflowBlockOptions"/> unbounded capacity.</param>
    /// <param name="flush">An action to flush the messages.</param>
    /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
    public OmfDataMessageBlock(
        ILogger logger,
        int capacity,
        Action<Message> flush,
        CancellationToken token)
        : base(logger, capacity, false, true, token)
    {
        ThrowHelper.ThrowIfArgumentNull(flush, nameof(flush));

        _flush = flush;
    }

    /// <inheritdoc/>
    protected override void Handle(Message message)
    {
        switch (message)
        {
            case GroupedDataMessage groupedData:
                ProcessGroupings(groupedData.Count, groupedData.Groupings, groupedData.StaticGroupings, groupedData.DynamicGroupingsWithPartitionKey, groupedData.MessageAction);
                break;
        }
    }

    /// <inheritdoc/>
    protected override Task HandleAsync(Message message)
    {
        throw new NotImplementedException();
    }

    private void ProcessGroupings(
        int count,
        Dictionary<string, (Classification Classification, List<object> Values)> groupings,
        List<StaticDataMessage> staticGroupings,
        Dictionary<PartitionKey, Dictionary<string, List<object>>> dynamicGroupingsWithPartitionKey,        
        MessageAction messageAction)
    {
        var dynamicStreamData = new List<DynamicStreamData>();
        var staticStreamData = new List<StaticStreamData>();
        var dynamicStreamDataWithPartitionKey = new Dictionary<PartitionKey, List<DynamicStreamData>>();        
        StaticStreamData links = null;

        if (groupings.TryGetValue(Tokens.Link, out var groupedLinks))
        {
            links = new StaticStreamData { Id = Tokens.Link, Values = groupedLinks.Values };
            groupings.Remove(Tokens.Link);
        }

        foreach (var (id, (classification, groupedValues)) in groupings)
        {
            switch (classification)
            {
                case Classification.Dynamic:
                    dynamicStreamData.Add(new DynamicStreamData { Id = id, Values = groupedValues });
                    break;

                case Classification.Static:
                    staticStreamData.Add(new StaticStreamData { Id = id, Values = groupedValues });
                    break;
            }
        }

        if (staticGroupings != null)
        {
            foreach (var staticInstance in staticGroupings)
            {
                staticStreamData.Add(new StaticStreamData
                {
                    Id = staticInstance.Id,
                    InstanceId = staticInstance.InstanceId,
                    Name = staticInstance.Name,
                    Description = staticInstance.Description,
                    DataSource = staticInstance.DataSource,
                    Properties = staticInstance.ExtendedPropertiesDefinition,
                    PropertyOverrides = staticInstance.PropertyOverrides,
                    Values = staticInstance.Instance == null ? null : [staticInstance.Instance],
                    Tags = staticInstance.Tags,
                    Metadata = staticInstance.Metadata,
                });
            }
        }

        if (dynamicGroupingsWithPartitionKey != null)
        {
            foreach (var (partitionKey, groups) in dynamicGroupingsWithPartitionKey)
            {
                if (!dynamicStreamDataWithPartitionKey.TryGetValue(partitionKey, out List<DynamicStreamData> dynamicStreamDataList))
                {
                    dynamicStreamDataList = [];
                    dynamicStreamDataWithPartitionKey.Add(partitionKey, dynamicStreamDataList);
                }

                foreach (var (id, groupedValues) in groups)
                {
                    dynamicStreamDataList.Add(new DynamicStreamData { Id = id, Values = groupedValues });
                }
            }
        }

        if (links != null)
        {
            staticStreamData.Add(links);
        }

        var valueCount = count;

        if (dynamicStreamDataWithPartitionKey.Count > 0)
        {
            foreach (var (partitionKey, dynamicStreamDataList) in dynamicStreamDataWithPartitionKey)
            { 
                var valueCountForThisPartitionKey = 0;

                foreach (var streamData in dynamicStreamDataList)
                {
                    valueCountForThisPartitionKey += streamData.Values.Count();
                }

                _flush(new OmfMessage<DynamicStreamData>(valueCountForThisPartitionKey, [.. dynamicStreamDataList], messageAction, partitionKey));
                valueCount -= valueCountForThisPartitionKey;
            }
        }

        if (dynamicStreamData.Count > 0)
        {
            _flush(new OmfMessage<DynamicStreamData>(valueCount, [.. dynamicStreamData], messageAction));
            valueCount = 0;
        }

        if (staticStreamData.Count > 0)
        {
            _flush(new OmfMessage<StaticStreamData>(valueCount, [.. staticStreamData], messageAction));
        }
    }
}
