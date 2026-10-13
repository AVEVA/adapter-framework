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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.DataFlow;
using AdapterFramework.Data.Framework.DataFlow.Strategy;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.EgressComponent;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Dispose logic is handled in the data flow blocks.")]
public class DataMessageProcessor : IMessageProcessor, IScopedMessageProcessor, IAwaitableMessageProcessor, IDisposable
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
    private readonly OmfAwaitableCoordinator _coordinator;
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
        IApplicationManifest applicationManifest = null,
        OmfAwaitableCoordinator awaitableCoordinator = null)
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
        awaitableCoordinator?.SetOmfVersion(omfVersion);
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
                logger, BlockCapacity, serializer, compressor, processMessageAction, CancellationToken.None, omfVersion, awaitableCoordinator);

            _schemaGroupingBlock = new SchemaGroupingBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg),
                MaxStreamsBatchCount, MaxTypesBatchCount, DefaultDataBulkTime * 4, CancellationToken.None);

            _instanceGroupingBlock = new InstanceGroupingBlock(logger, BlockCapacity, msg => _serializationBlock.Post(msg),
                msg => _schemaGroupingBlock.Post(msg), maximumDataBatchCount, flushTime, CancellationToken.None);

            _coordinator = awaitableCoordinator;
            _coordinator?.SetSealHandler(PostSealBarriers);
        }
    }

    #endregion

    #region Public Methods

    /// <inheritdoc/>
    public bool TryCreateAwaitableScope(OmfAwaitableScopeOptions options, out IAwaitableMessageScope scope)
    {
        if (!TryCreateScope(options, out var token))
        {
            scope = null;
            return false;
        }

        scope = new AwaitableMessageScope(this, token);
        return true;
    }

    /// <inheritdoc/>
    public bool TryCreateScope(OmfAwaitableScopeOptions options, out ScopeToken scope)
    {
        if (_coordinator is null)
        {
            throw new NotSupportedException("Awaitable scopes require an OMF 2.0 data pipeline with an awaitable coordinator.");
        }

        var created = _coordinator.TryCreateScope(options, out var state);
        scope = state;
        return created;
    }

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction) => WriteType(dataType, messageAction, null);

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataType, nameof(dataType));

        Post(_typesStreamsGroupingBlock, _schemaGroupingBlock, new OmfMessage<DataType>(1, [dataType], messageAction), CountTypeItems([dataType]), scope);
    }

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction) => WriteTypes(dataTypes, messageAction, null);

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));

        Post(_typesStreamsGroupingBlock, _schemaGroupingBlock, new OmfMessage<DataType>(dataTypes.Length, dataTypes, messageAction), CountTypeItems(dataTypes), scope);
    }

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction) => WriteStream(dataStream, messageAction, null);

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

        Post(_typesStreamsGroupingBlock, _schemaGroupingBlock, new OmfMessage<DataStream>(1, [dataStream], messageAction), 1, scope);
    }

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction) => WriteStreams(dataStreams, messageAction, null);

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));

        Post(_typesStreamsGroupingBlock, _schemaGroupingBlock, new OmfMessage<DataStream>(dataStreams.Length, dataStreams, messageAction), dataStreams.Length, scope);
    }

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction) where T : class =>
        WriteValue(id, classification, instance, messageAction, null);

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        Post(_dataGroupingBlock, _instanceGroupingBlock, new DataMessage(id, classification, instance, messageAction), 1, scope);
    }

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class =>
        WriteDynamicValue(id, instance, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        Post(_dataGroupingBlock, _instanceGroupingBlock, new DataMessage(id, Classification.Dynamic, instance, messageAction, partitionKey), 1, scope);
    }

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction) where T : class =>
        WriteValues(id, classification, instances, messageAction, null);

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        Post(_dataGroupingBlock, _instanceGroupingBlock, new BulkDataMessage(id, classification, instances, messageAction), instances.Count, scope);
    }

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class =>
        WriteDynamicValues(id, instances, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        Post(_dataGroupingBlock, _instanceGroupingBlock, new BulkDataMessage(id, Classification.Dynamic, instances, messageAction, partitionKey), instances.Count, scope);
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction) where T : class =>
        WriteStaticValue(id, extendedPropertyDefinitions, propertyOverrides, instance, metadata, messageAction, null);

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        Post(_dataGroupingBlock, _instanceGroupingBlock, new StaticDataMessage(id, extendedPropertyDefinitions, propertyOverrides, metadata, instance, messageAction), 1, scope);
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteStaticValue(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, null);

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships,
        MessageAction messageAction, ScopeToken scope) where T : class
    {
        typeId = NormalizeStaticTypeId(typeId, messageAction);
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        // Callers are responsible for passing instance as null when an entity deletion is intended.
        var staticMessage = new StaticDataMessage(typeId, id, name, description, dataSource, tags, extendedPropertyDefinitions, propertyOverrides, metadata, instance, messageAction)
        {
            Relationships = relationships,
        };

        Post(_dataGroupingBlock, _instanceGroupingBlock, staticMessage, 1 + (relationships?.Count ?? 0), scope);
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null,
        List<string> tags = null, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteStaticValue(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction, null);

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata,
        List<string> tags, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, MessageAction messageAction, ScopeToken scope) where T : class
    {
        typeId = NormalizeStaticTypeId(typeId, messageAction);
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        // Callers are responsible for passing instance as null when an entity deletion is intended.
        var staticMessage = new StaticDataMessage(typeId, id, name, description, dataSource, tags, null, propertyOverrides, metadata, instance, messageAction);
        Post(_dataGroupingBlock, _instanceGroupingBlock, staticMessage, 1, scope);
    }

    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteEvent(id, typeId, name, description, dataSource, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, null);

    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        var eventMessage = new EventMessage(typeId, id, name, description, dataSource, startTime, endTime, tags, extendedPropertyDefinitions, propertyOverrides,
            metadata, instance, relationships, messageAction);

        Post(null, _instanceGroupingBlock, eventMessage, 1 + (relationships?.Count ?? 0), scope);
    }

    public void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default) => WriteSchemaRelationship(link, messageAction, null);

    public void WriteSchemaRelationship(Link link, MessageAction messageAction, ScopeToken scope)
    {
        Post(null, _schemaGroupingBlock, new RelationshipMessage(link, messageAction), 1, scope);
    }

    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default) => WriteInstanceRelationship(link, messageAction, null);

    public void WriteInstanceRelationship(Link link, MessageAction messageAction, ScopeToken scope)
    {
        Post(null, _instanceGroupingBlock, new RelationshipMessage(link, messageAction), 1, scope);
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

    // A type's embedded relationships become separate relationship items in the schema grouping block.
    private static int CountTypeItems(DataType[] dataTypes)
    {
        var count = dataTypes.Length;
        foreach (var dataType in dataTypes)
        {
            count += dataType?.Relationships?.Count ?? 0;
        }

        return count;
    }

    private static string NormalizeStaticTypeId(string typeId, MessageAction messageAction)
    {
        if (messageAction != MessageAction.Delete)
        {
            ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeId, nameof(typeId));
            return typeId;
        }

        // a blank typeid value is normalized to null to be omitted during serialization.
        return string.IsNullOrWhiteSpace(typeId) ? null : typeId;
    }

    private void Post(BaseBlock<Message> omf12Block, BaseBlock<Message> omf20Block, Message envelope, int itemCount, ScopeToken scope)
    {
        if (scope is null)
        {
            omf12Block?.Post(envelope);
            omf20Block?.Post(envelope);
            return;
        }

        envelope.Scope = scope;
        _coordinator?.RecordAdmitted(scope, itemCount);
        if (omf20Block?.Post(envelope) != true && itemCount > 0)
        {
            _coordinator?.RecordItemsDiscarded(scope, itemCount, new OmfOutcomeReason(
                OmfReasonCode.PostRejected,
                string.Create(CultureInfo.InvariantCulture, $"{omf20Block?.GetType().Name ?? "No grouping block"} refused {itemCount} items.")));
        }
    }

    private void PostSealBarriers(OmfAwaitableScopeState scope)
    {
        // Barriers go to both blocks so serialization always knows how many to expect.
        var schemaPosted = _schemaGroupingBlock.Post(new ScopeSealBarrier(scope, scope.Options.FlushOnSeal, 2));
        var instancePosted = _instanceGroupingBlock.Post(new ScopeSealBarrier(scope, scope.Options.FlushOnSeal, 2));
        if (!schemaPosted || !instancePosted)
        {
            _coordinator.RecordItemsDiscarded(scope, 1, new OmfOutcomeReason(OmfReasonCode.PostRejected, "A grouping block refused the scope's seal barrier."));
        }
    }

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
