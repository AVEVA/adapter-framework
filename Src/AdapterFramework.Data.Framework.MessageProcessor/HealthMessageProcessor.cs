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
using System.Threading;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.DataFlow;
using AdapterFramework.Data.Framework.DataFlow.Strategy;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;

namespace AdapterFramework.Data.Framework.MessageProcessor;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
public class HealthMessageProcessor : IHealthMessageProcessor, IDisposable
{
    #region Private Constants

    private const int BlockCapacity = DataflowBlockOptions.Unbounded;
    private const int InitialUpperLimit = 10_000;
    private const double DeltaLimitFactor = 0.010;
    private const double LowerBoundFactor = 0.988;
    private const double UpperBoundFactor = 0.988;
    private const int MaxMessageSizeBytes = 192 * 1024;
    private const int MaxDataBatchCount = 30_000;
    private const int MaxStreamsBatchCount = 30_000;
    private const int MaxTypesBatchCount = 5_000;
    private const int FlushTime = 1000;

    #endregion

    #region Private Fields

    private readonly TypesStreamsGroupingBlock _typesStreamsGroupingBlock;
    private readonly DataGroupingBlock _dataGroupingBlock;
    private readonly OmfDataMessageBlock _omfDataMessageBlock;
    private readonly SerializationBlock _serializationBlock;
    private bool _disposed;

    #endregion

    #region Public Constructor

    public HealthMessageProcessor(ILogger logger, ISerializer serializer, ICompressor compressor, IOmfHealthEndpointManager endpointManager)
    {
        ThrowHelper.ThrowIfArgumentNull(endpointManager, nameof(endpointManager));

        endpointManager.Initialize(EdgeSystemConstants.SystemComponentId, EdgeSystemConstants.HealthEndpointsFacetName);

        var tuningParameters = new TuningParameters
        {
            InitialUpperLimit = InitialUpperLimit,
            DeltaLimitFactor = DeltaLimitFactor,
            LowerBoundFactor = LowerBoundFactor,
            UpperBoundFactor = UpperBoundFactor,
        };

        var dataBatchingStrategyOptimizer = new BatchSizeOptimizer(tuningParameters, MaxMessageSizeBytes, msg => _dataGroupingBlock.Post(new StateMessage(msg)));
        var streamsBatchingStrategyOptimizer = new BatchSizeOptimizer(tuningParameters, MaxMessageSizeBytes, msg => _typesStreamsGroupingBlock.Post(new StateMessage(msg)));

        _typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg), MaxStreamsBatchCount, MaxTypesBatchCount, FlushTime, CancellationToken.None);
        _dataGroupingBlock = new DataGroupingBlock(logger, BlockCapacity, msg => _omfDataMessageBlock.Post(msg), msg => _typesStreamsGroupingBlock.Post(msg), MaxDataBatchCount, FlushTime, CancellationToken.None);
        _serializationBlock = new SerializationBlock(dataBatchingStrategyOptimizer, streamsBatchingStrategyOptimizer, MaxMessageSizeBytes, logger, BlockCapacity, serializer, compressor, endpointManager.SendMessage, CancellationToken.None);
        _omfDataMessageBlock = new OmfDataMessageBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg), CancellationToken.None);
    }

    #endregion

    #region Public Properties

    /// <inheritdoc/>
    public MetadataInfo StreamMetadataLevel { get; set; }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public void WriteHealthTypes(DataType[] dataTypes, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));

        _typesStreamsGroupingBlock.Post(new OmfMessage<DataType>(dataTypes.Length, dataTypes, messageAction));
    }

    /// <inheritdoc/>
    public void WriteHealthStreams(DataStream[] dataStreams, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));

        if (StreamMetadataLevel == MetadataInfo.None)
        {
            Array.ForEach(dataStreams, x => x.Metadata = null);
        }

        _typesStreamsGroupingBlock.Post(new OmfMessage<DataStream>(dataStreams.Length, dataStreams, messageAction));
    }

    /// <inheritdoc/>
    public void WriteHealthValue<T>(string id, Classification classification, T instance, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        _dataGroupingBlock.Post(new DataMessage(id, classification, instance, messageAction));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        _typesStreamsGroupingBlock?.Dispose();
        _dataGroupingBlock?.Dispose();
        _omfDataMessageBlock?.Dispose();
        _serializationBlock?.Dispose();

        _disposed = true;
    }

    #endregion
}
