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
using System.Threading.Tasks.Dataflow;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.DataFlow;
using AdapterFramework.Data.Framework.DataFlow.Strategy;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.EgressComponent;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Dispose logic is handled in the data flow blocks.")]
public class DataMessageProcessor : IMessageProcessor, IDisposable
{
    #region Private Constants

    private const int BlockCapacity = DataflowBlockOptions.Unbounded;
    private const int InitialUpperLimit = 10_000;
    private const double DeltaLimitFactor = 0.010;
    private const double LowerBoundFactor = 0.988;
    private const double UpperBoundFactor = 0.988;
    private const int InitialUpperLimitOmf20 = 25_000;
    private const double DeltaLimitFactorOmf20 = 0.008;
    private const double LowerBoundFactorOmf20 = 0.975;
    private const double UpperBoundFactorOmf20 = 0.990;
    private const int MaxMessageSizeBytes = 192 * 1024;
    private const int MaxMessageSizeBytesOmf13 = 512 * 1024;
    private const int MaxDataBatchCount = 50_000;
    private const int MaxDataBatchCountOmf13 = 120_000;
    private const int MaxStreamsBatchCount = 60_000;
    private const int MaxTypesBatchCount = 5_000;

    #endregion

    #region Private Fields

    private readonly DataGroupingBlock _dataGroupingBlock;
    private readonly InstanceGroupingBlock _instanceGroupingBlock;
    private readonly SchemaGroupingBlock _schemaGroupingBlock;
    private readonly TypesStreamsGroupingBlock _typesStreamsGroupingBlock;
    private readonly OmfDataMessageBlock _omfDataMessageBlock;
    private readonly SerializationBlock _serializationBlock;
    private bool _disposed;

    #endregion

    #region Public Constructor

    public DataMessageProcessor(
        ILogManager logManager,
        ISerializer serializer,
        ICompressor compressor,
        IOmfDataEndpointManager dataEndpointManager,
        IEgressComponentIdService egressComponentIdService,
        IConfigurationProvider configurationProvider,
        IFailoverDataMessageProcessor failoverDataMessageProcessor = null,
        IApplicationManifest applicationManifest = null)
    {
        ThrowHelper.ThrowIfArgumentNull(logManager, nameof(logManager));
        ThrowHelper.ThrowIfArgumentNull(egressComponentIdService, nameof(egressComponentIdService));
        ThrowHelper.ThrowIfArgumentNull(dataEndpointManager, nameof(dataEndpointManager));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));

        var logger = logManager.GetOrCreateLogger(egressComponentIdService.ComponentId);

        var processMessageAction = GetProcessMessageAction(dataEndpointManager, failoverDataMessageProcessor);

        var flushTime = DefaultDataBulkTime;
        if (configurationProvider.TryGetConfiguration<BufferingConfiguration>(SystemComponentId, BufferingFacetName, out var bufferingConfiguration, out var getErrors))
        {
            flushTime = (int)bufferingConfiguration.MaxDataBulkTime.TotalMilliseconds;
        }

        var omfVersion = applicationManifest?.OmfVersion ?? OmfVersion.Omf12;
        var tuningParameters = omfVersion == OmfVersion.Omf20
            ? new TuningParameters
            {
                InitialUpperLimit = InitialUpperLimitOmf20,
                DeltaLimitFactor = DeltaLimitFactorOmf20,
                LowerBoundFactor = LowerBoundFactorOmf20,
                UpperBoundFactor = UpperBoundFactorOmf20,
            }
            : new TuningParameters
            {
                InitialUpperLimit = InitialUpperLimit,
                DeltaLimitFactor = DeltaLimitFactor,
                LowerBoundFactor = LowerBoundFactor,
                UpperBoundFactor = UpperBoundFactor,
            };

        var maximumMessageSizeBytes = omfVersion == OmfVersion.Omf20 ? MaxMessageSizeBytesOmf13 : MaxMessageSizeBytes;
        var maximumDataBatchCount = omfVersion == OmfVersion.Omf20 ? MaxDataBatchCountOmf13 : MaxDataBatchCount;

        if (omfVersion == OmfVersion.Omf12)
        {
            var dataBatchingStrategyOptimizer = new BatchSizeOptimizer(tuningParameters, maximumMessageSizeBytes,
                msg => _dataGroupingBlock.Post(new StateMessage(msg)));

            var streamsBatchingStrategyOptimizer = new BatchSizeOptimizer(tuningParameters, maximumMessageSizeBytes,
                msg => _typesStreamsGroupingBlock.Post(new StateMessage(msg)));

            _serializationBlock = new SerializationBlock(dataBatchingStrategyOptimizer, streamsBatchingStrategyOptimizer, maximumMessageSizeBytes,
                logger, BlockCapacity, serializer, compressor, processMessageAction, CancellationToken.None, omfVersion);

            _typesStreamsGroupingBlock = new TypesStreamsGroupingBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg),
                MaxStreamsBatchCount, MaxTypesBatchCount, DefaultDataBulkTime, CancellationToken.None);

            _dataGroupingBlock = new DataGroupingBlock(logger, BlockCapacity, msg => _omfDataMessageBlock.Post(msg),
                msg => _typesStreamsGroupingBlock.Post(msg), maximumDataBatchCount, flushTime, CancellationToken.None);

            _omfDataMessageBlock = new OmfDataMessageBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg), CancellationToken.None);
        }
        else
        {
            var dataBatchingStrategyOptimizer = new BatchSizeOptimizer(tuningParameters, maximumMessageSizeBytes,
                msg => _instanceGroupingBlock.Post(new StateMessage(msg)));

            var schemaBatchingStrategyOptimizer = new BatchSizeOptimizer(tuningParameters, maximumMessageSizeBytes,
                msg => _schemaGroupingBlock.Post(new StateMessage(msg)));

            _serializationBlock = new SerializationBlock(dataBatchingStrategyOptimizer, schemaBatchingStrategyOptimizer, maximumMessageSizeBytes,
                logger, BlockCapacity, serializer, compressor, processMessageAction, CancellationToken.None, omfVersion);

            _schemaGroupingBlock = new SchemaGroupingBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg),
                MaxStreamsBatchCount, MaxTypesBatchCount, DefaultDataBulkTime * 4, CancellationToken.None);

            _instanceGroupingBlock = new InstanceGroupingBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg),
                msg => _schemaGroupingBlock.Post(msg), maximumDataBatchCount, flushTime, CancellationToken.None);
        }
    }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataType, nameof(dataType));

        _typesStreamsGroupingBlock?.Post(new OmfMessage<DataType>(1, [dataType], messageAction));
        _schemaGroupingBlock?.Post(new OmfMessage<DataType>(1, [dataType], messageAction));
    }

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));

        _typesStreamsGroupingBlock?.Post(new OmfMessage<DataType>(dataTypes.Length, dataTypes, messageAction));
        _schemaGroupingBlock?.Post(new OmfMessage<DataType>(dataTypes.Length, dataTypes, messageAction));
    }

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

        _typesStreamsGroupingBlock?.Post(new OmfMessage<DataStream>(1, [dataStream], messageAction));
        _schemaGroupingBlock?.Post(new OmfMessage<DataStream>(1, [dataStream], messageAction));
    }

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));

        _typesStreamsGroupingBlock?.Post(new OmfMessage<DataStream>(dataStreams.Length, dataStreams, messageAction));
        _schemaGroupingBlock?.Post(new OmfMessage<DataStream>(dataStreams.Length, dataStreams, messageAction));
    }

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        _dataGroupingBlock?.Post(new DataMessage(id, classification, instance, messageAction));
        _instanceGroupingBlock?.Post(new DataMessage(id, classification, instance, messageAction));
    }

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        _dataGroupingBlock?.Post(new DataMessage(id, Classification.Dynamic, instance, messageAction, partitionKey));
        _instanceGroupingBlock?.Post(new DataMessage(id, Classification.Dynamic, instance, messageAction, partitionKey));
    }

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        _dataGroupingBlock?.Post(new BulkDataMessage(id, classification, instances, messageAction));
        _instanceGroupingBlock?.Post(new BulkDataMessage(id, classification, instances, messageAction));
    }

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        _dataGroupingBlock?.Post(new BulkDataMessage(id, Classification.Dynamic, instances, messageAction, partitionKey));
        _instanceGroupingBlock?.Post(new BulkDataMessage(id, Classification.Dynamic, instances, messageAction, partitionKey));
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        _dataGroupingBlock?.Post(new StaticDataMessage(id, extendedPropertyDefinitions, propertyOverrides, metadata, instance, messageAction));
        _instanceGroupingBlock?.Post(new StaticDataMessage(id, extendedPropertyDefinitions, propertyOverrides, metadata, instance, messageAction));
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default) where T : class
    {
        if (messageAction != MessageAction.Delete)
        {
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
        }
        else
        {
            // a blank typeid value is normalized to null to be omitted during serialization.
            if (string.IsNullOrWhiteSpace(typeId))
            {
                typeId = null;
            }
        }

        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        // Callers are responsible for passing instance as null when an entity deletion is intended.
        var staticMessage = new StaticDataMessage(typeId, id, name, description, dataSource, tags, extendedPropertyDefinitions, propertyOverrides, metadata, instance, messageAction)
        {
            Relationships = relationships,
        };
        _dataGroupingBlock?.Post(staticMessage);
        _instanceGroupingBlock?.Post(staticMessage);
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null,
        List<string> tags = null, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default) where T : class
    {
        if (messageAction != MessageAction.Delete)
        {
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
        }
        else
        {
            // a blank typeid value is normalized to null to be omitted during serialization.
            if (string.IsNullOrWhiteSpace(typeId))
            {
                typeId = null;
            }
        }

        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        // Callers are responsible for passing instance as null when an entity deletion is intended.
        var staticMessage = new StaticDataMessage(typeId, id, name, description, dataSource, tags, null, propertyOverrides, metadata, instance, messageAction);
        _dataGroupingBlock?.Post(staticMessage);
        _instanceGroupingBlock?.Post(staticMessage);
    }

    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        var eventMessage = new EventMessage(typeId, id, name, description, dataSource, startTime, endTime, tags, extendedPropertyDefinitions, propertyOverrides,
            metadata, instance, relationships, messageAction);

        _instanceGroupingBlock?.Post(eventMessage);
    }

    public void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default)
    {
        _schemaGroupingBlock?.Post(new RelationshipMessage(link, messageAction));
    }

    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default)
    {
        _instanceGroupingBlock?.Post(new RelationshipMessage(link, messageAction));
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

        _schemaGroupingBlock?.Dispose();
        _typesStreamsGroupingBlock?.Dispose();
        _instanceGroupingBlock?.Dispose();
        _dataGroupingBlock?.Dispose();
        _omfDataMessageBlock?.Dispose();
        _serializationBlock?.Dispose();

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private static Action<ISerializedOmfMessage> GetProcessMessageAction(IOmfDataEndpointManager dataEndpointManager,
        IFailoverDataMessageProcessor failoverDataMessageProcessor)
    {
        if (failoverDataMessageProcessor == null)
        {
            dataEndpointManager.Initialize(OmfEgressComponentId, DataEndpointsFacetName);
            return dataEndpointManager.SendMessage;
        }

        failoverDataMessageProcessor.Initialize();
        return failoverDataMessageProcessor.ProcessOmfMessage;
    }

    #endregion
}
