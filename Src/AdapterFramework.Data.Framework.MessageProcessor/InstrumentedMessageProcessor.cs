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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Messages.Awaitable;

namespace AdapterFramework.Data.Framework.MessageProcessor;

public class InstrumentedMessageProcessor : IInstrumentedMessageProcessor, IScopedMessageProcessor, IAwaitableMessageProcessor
{
    #region Private Fields

    private readonly IScopedMessageProcessor _messageProcessor;
    private readonly ConcurrentDictionary<string, (DataType DataType, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> _dataTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DataStream DataStream, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> _dataStreams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (Link Link, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> _relationships = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<DataStream, string> _preparedStreamIds = [];
    private readonly ConcurrentDictionary<string, byte> _entityIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _metaDataDictionary;
    private readonly Dictionary<StreamProperties, Action<PropertyDefinitionOverride>> _propertyOverrideActions;
    private readonly string _componentId;
    private readonly ILogger _logger;

    private string _streamIdPrefix;
    private int _streamCount;
    private int _typeCount;

    // Cumulative count of OMF 2.0 events accepted (deletes excluded), published as EventCount.
    private long _eventCount;

    // Data events since the last GetAndResetEventsCounter() call, published as IORate.
    private long _ioEventsCount;
    private long _cacheOrderSequence;

    #endregion

    #region Public Constructor

    public InstrumentedMessageProcessor(IMessageProcessor messageProcessor, ILogger logger, string componentId, string componentType)
    {
        _messageProcessor = messageProcessor?.AsScoped();
        _logger = logger;
        _componentId = componentId;

        _metaDataDictionary = new Dictionary<string, object>
        {
            { EdgeSystemConstants.AdapterTypeString, componentType },
        };

        _propertyOverrideActions = new Dictionary<StreamProperties, Action<PropertyDefinitionOverride>>
        {
            { StreamProperties.Description, (x) => x.Description = null },
            { StreamProperties.Minimum, (x) => x.Minimum = null },
            { StreamProperties.Maximum, (x) => x.Maximum = null },
            { StreamProperties.Uom, (x) => x.Uom = null },
            { StreamProperties.Interpolation, (x) => x.Interpolation = null },
        };
    }

    #endregion

    #region Public Properties

    public MetadataInfo StreamMetadataLevel { get; set; }

    public StreamProperties IncludeSourceProperties { get; set; }

    #endregion

    #region Public Methods

    #region IMessageProcessor Implementation

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
    public bool TryCreateScope(OmfAwaitableScopeOptions options, out ScopeToken scope) => _messageProcessor.TryCreateScope(options, out scope);

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction) => WriteType(dataType, messageAction, null, null);

    /// <inheritdoc/>
    public void WriteType(DataType dataType, PartitionKey partitionKey, MessageAction messageAction) => WriteType(dataType, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataType, nameof(dataType));
        AdapterMessageProcessor.ThrowIfInvalidPartitionKey(partitionKey);

        PrepareDataType(dataType);

        _messageProcessor.WriteType(dataType, messageAction, partitionKey, scope);

        CacheDataTypeUpdateInstrumentation(dataType, messageAction, PartitionTargets.From(partitionKey));
    }

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction) => WriteTypes(dataTypes, messageAction, null, null);

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, PartitionKey partitionKey, MessageAction messageAction) => WriteTypes(dataTypes, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));
        AdapterMessageProcessor.ThrowIfInvalidPartitionKey(partitionKey);

        foreach (var dataType in dataTypes)
        {
            PrepareDataType(dataType);
        }

        _messageProcessor.WriteTypes(dataTypes, messageAction, partitionKey, scope);

        foreach (var dataType in dataTypes)
        {
            CacheDataTypeUpdateInstrumentation(dataType, messageAction, PartitionTargets.From(partitionKey));
        }
    }

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction) => WriteStream(dataStream, messageAction, null, null);

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, PartitionKey partitionKey, MessageAction messageAction) => WriteStream(dataStream, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));
        AdapterMessageProcessor.ThrowIfInvalidPartitionKey(partitionKey);

        PrepareDataStream(dataStream);

        _messageProcessor.WriteStream(dataStream, messageAction, partitionKey, scope);

        CacheDataStreamUpdateInstrumentation(dataStream, messageAction, PartitionTargets.From(partitionKey));
    }

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction) => WriteStreams(dataStreams, messageAction, null, null);

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, PartitionKey partitionKey, MessageAction messageAction) => WriteStreams(dataStreams, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));
        AdapterMessageProcessor.ThrowIfInvalidPartitionKey(partitionKey);

        foreach (var dataStream in dataStreams)
        {
            ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

            PrepareDataStream(dataStream);
        }

        _messageProcessor.WriteStreams(dataStreams, messageAction, partitionKey, scope);

        foreach (var dataStream in dataStreams)
        {
            CacheDataStreamUpdateInstrumentation(dataStream, messageAction, PartitionTargets.From(partitionKey));
        }
    }

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction) where T : class =>
        WriteValue(id, classification, instance, messageAction, null);

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction, ScopeToken scope) where T : class
    {
        _messageProcessor.WriteValue(GetPrefixedOrSanitizedIdentifier(id, classification), classification, instance, messageAction, scope);

        IncrementIoEventsCount();
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public void DeleteDynamicValuesInRange(string id, StreamingDataRange range, PartitionKey? partitionKey = null) =>
        DeleteDynamicValuesInRange(id, range, partitionKey, null);

    /// <inheritdoc/>
    public void DeleteDynamicValuesInRange(string id, StreamingDataRange range, PartitionKey? partitionKey, ScopeToken scope)
    {
        _messageProcessor.DeleteDynamicValuesInRange(GetPrefixedOrSanitizedIdentifier(id, Classification.Dynamic), range, partitionKey, scope);
    }

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class =>
        WriteDynamicValue(id, instance, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope) where T : class
    {
        _messageProcessor.WriteDynamicValue(GetPrefixedOrSanitizedIdentifier(id, Classification.Dynamic), instance, messageAction, partitionKey, scope);

        IncrementIoEventsCount();
    }

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction) where T : class =>
        WriteValues(id, classification, instances, messageAction, null);

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        _messageProcessor.WriteValues(GetPrefixedOrSanitizedIdentifier(id, classification), classification, instances, messageAction, scope);

        AddToIoEventsCount(instances.Count);
    }

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class =>
        WriteDynamicValues(id, instances, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        _messageProcessor.WriteDynamicValues(GetPrefixedOrSanitizedIdentifier(id, Classification.Dynamic), instances, messageAction, partitionKey, scope);

        AddToIoEventsCount(instances.Count);
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction) where T : class =>
        WriteStaticValue(id, extendedPropertyDefinitions, propertyOverrides, instance, metadata, messageAction, null);

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction, ScopeToken scope) where T : class
    {
        _messageProcessor.WriteStaticValue(id.ToOmfIdentifier(), extendedPropertyDefinitions, propertyOverrides, instance, metadata, messageAction, scope);

        IncrementIoEventsCount();
    }

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteStaticValue(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, null);

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction,
        ScopeToken scope) where T : class
    {
        var entityId = id.ToOmfIdentifier();

        _messageProcessor.WriteStaticValue(ToOmfTypeIdOrNull(typeId, messageAction), entityId, name, description, GetDataSource(dataSource, id), extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, scope);

        TrackEntityIdentity(entityId, messageAction);
        IncrementIoEventsCount();
    }

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags = null,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteStaticValue(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction, null);

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, MessageAction messageAction, ScopeToken scope) where T : class
    {
        var entityId = id.ToOmfIdentifier();

        _messageProcessor.WriteStaticValue(ToOmfTypeIdOrNull(typeId, messageAction), entityId, name, description, GetDataSource(dataSource, id), instance, metadata, tags, propertyOverrides, messageAction, scope);

        TrackEntityIdentity(entityId, messageAction);
        IncrementIoEventsCount();
    }

    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteEvent(id, typeId, name, description, dataSource, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, null);

    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction, ScopeToken scope) where T : class
    {
        var eventId = id.ToOmfIdentifier();

        _messageProcessor.WriteEvent(eventId, typeId.ToOmfIdentifier(), name, description, GetDataSource(dataSource, id), startTime, endTime,
            extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, scope);

        if (messageAction != MessageAction.Delete)
        {
            Interlocked.Increment(ref _eventCount);
        }

        IncrementIoEventsCount();
    }

    public void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default) => WriteSchemaRelationship(link, messageAction, null, null);

    /// <inheritdoc/>
    public void WriteSchemaRelationship(Link link, PartitionKey partitionKey, MessageAction messageAction = MessageAction.Default) =>
        WriteSchemaRelationship(link, messageAction, partitionKey, null);

    public void WriteSchemaRelationship(Link link, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
    {
        ThrowHelper.ThrowIfArgumentNull(link, nameof(link));
        AdapterMessageProcessor.ThrowIfInvalidPartitionKey(partitionKey);

        _messageProcessor.WriteSchemaRelationship(link, messageAction, partitionKey, scope);

        CacheRelationshipUpdateInstrumentation(link, messageAction, PartitionTargets.From(partitionKey));
    }

    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default) => WriteInstanceRelationship(link, messageAction, null);

    public void WriteInstanceRelationship(Link link, MessageAction messageAction, ScopeToken scope)
    {
        _messageProcessor.WriteInstanceRelationship(link, messageAction, scope);
    }

    #endregion

    #region IInstrumentedOmfMessageProcessor Implementation

    /// <inheritdoc/>
    public void SetStreamIdPrefix(string streamIdPrefix)
    {
        _streamIdPrefix = !string.IsNullOrWhiteSpace(streamIdPrefix) ? streamIdPrefix.ToOmfIdentifier() : null;
    }

    /// <inheritdoc/>
    public int GetStreamCount()
    {
        return _streamCount;
    }

    /// <inheritdoc/>
    public int GetTypeCount()
    {
        return _typeCount;
    }

    /// <inheritdoc/>
    public int GetAssetCount()
    {
        // Counted from the identity set on purpose: a separate counter updated next to the set can drift permanently once there is no lock.
        return _entityIds.Count;
    }

    /// <inheritdoc/>
    public long GetEventCount()
    {
        return Interlocked.Read(ref _eventCount);
    }

    /// <inheritdoc/>
    public long GetAndResetEventsCounter()
    {
        return Interlocked.Exchange(ref _ioEventsCount, 0);
    }

    /// <inheritdoc/>
    public void ClearCounters()
    {
        Interlocked.Exchange(ref _typeCount, 0);
        Interlocked.Exchange(ref _streamCount, 0);
        Interlocked.Exchange(ref _ioEventsCount, 0);
        Interlocked.Exchange(ref _eventCount, 0);

        // Tracked asset identities are retained on purpose: assets still exist downstream after a stop, so AssetCount must not
        // fall to zero when an adapter does not re-send its static values on start.
    }

    #endregion

    /// <inheritdoc/>
    public void ResendTypesAndStreams()
    {
        Resend(_dataTypes.Values, (dataType, messageAction) => _messageProcessor.WriteType(dataType, messageAction, null, null),
            (dataType, partitionKey, messageAction) => _messageProcessor.WriteType(dataType, messageAction, partitionKey, null));
        Resend(_dataStreams.Values, (dataStream, messageAction) => _messageProcessor.WriteStream(dataStream, messageAction, null, null),
            (dataStream, partitionKey, messageAction) => _messageProcessor.WriteStream(dataStream, messageAction, partitionKey, null));
        Resend(_relationships.Values, (link, messageAction) => _messageProcessor.WriteSchemaRelationship(link, messageAction, null, null),
            (link, partitionKey, messageAction) => _messageProcessor.WriteSchemaRelationship(link, messageAction, partitionKey, null));
    }

    /// <inheritdoc/>
    public void ClearStreamsCollection()
    {
        _dataStreams.Clear();
    }

    /// <summary>
    /// Handles changes made to <see typeparamref="TSelection"/> configuration to update stream count.
    /// </summary>
    /// <typeparam name="TSelection">Type of data selection configuration.</typeparam>
    /// <param name="configuration">New <see typeparamref="TSelection"/> configuration.</param>
    public void ProcessDataSelectionConfigurationChanges<TSelection>(TSelection[] configuration) where TSelection : IDataSelectionConfiguration
    {
        if (configuration == null)
        {
            Interlocked.Exchange(ref _streamCount, 0);
            ClearStreamsCollection();
            return;
        }

        var selectedIds = configuration.Where(x => x.Selected).Select(x => x.StreamId.ToPrefixedOmfIdentifier(_streamIdPrefix)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (streamId, _) in _dataStreams)
        {
            if (!selectedIds.Contains(streamId))
            {
                _dataStreams.TryRemove(streamId, out _);
                Interlocked.Decrement(ref _streamCount);
            }
        }
    }

    #endregion

    #region Private Methods

    private static void EncodeUnsupportedCharactersInReferences(DataType dataType)
    {
        foreach (var typeProperty in dataType.Properties?.Values ?? Enumerable.Empty<PropertyDefinition>())
        {
            if (!string.IsNullOrEmpty(typeProperty.RefTypeId))
            {
                typeProperty.RefTypeId = typeProperty.RefTypeId.ToOmfIdentifier();
            }
        }
    }

    // OMF 2.0 delete payloads must not carry a typeid, so a null/empty typeId is allowed and passed through for delete actions.
    private static string ToOmfTypeIdOrNull(string typeId, MessageAction messageAction)
    {
        return messageAction == MessageAction.Delete && string.IsNullOrWhiteSpace(typeId)
            ? null
            : typeId.ToOmfIdentifier();
    }

    // Writing one key at a time keeps each partition's items in cache order and lets SchemaGroupingBlock batch them.
    private static void Resend<T>(
        IEnumerable<(T Item, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> entries,
        Action<T, MessageAction> writeWithoutKey,
        Action<T, PartitionKey, MessageAction> writeWithKey)
    {
        var ordered = entries.OrderBy(x => x.Sequence).ToList();

        foreach (var (item, messageAction, targets, _) in ordered)
        {
            if (targets.IncludeDefaultPartition)
            {
                writeWithoutKey(item, messageAction);
            }
        }

        foreach (var partitionKey in ordered.SelectMany(x => x.Targets.Keys).Distinct().Order())
        {
            foreach (var (item, messageAction, targets, _) in ordered)
            {
                if (Array.IndexOf(targets.Keys, partitionKey) >= 0)
                {
                    writeWithKey(item, partitionKey, messageAction);
                }
            }
        }
    }

    private static void PrepareDataType(DataType dataType)
    {
        dataType.Id = dataType.Id.ToOmfIdentifier();

        EncodeUnsupportedCharactersInReferences(dataType);
    }

    private void PrepareDataStream(DataStream dataStream)
    {
        // Callers may rewrite the same object once per partition key; prefixing it again would change its ID.
        // Weak-table operations do not make preparation atomic; concurrent writes of the same object are unsupported.
        if (!_preparedStreamIds.TryGetValue(dataStream, out var preparedId) || !string.Equals(preparedId, dataStream.Id, StringComparison.Ordinal))
        {
            dataStream.Id = dataStream.Id.ToPrefixedOmfIdentifier(_streamIdPrefix);
            _preparedStreamIds.AddOrUpdate(dataStream, dataStream.Id);
        }

        dataStream.TypeId = dataStream.TypeId?.ToOmfIdentifier();

        AddMetadataValues(dataStream);

        dataStream.DataSource = GetDataSource(dataStream.DataSource, dataStream.Id);
        ExcludeStreamProperties(dataStream);
    }

    private void CacheRelationshipUpdateInstrumentation(Link link, MessageAction messageAction, PartitionTargets targets)
    {
        var key = GetRelationshipKey(link);

        _relationships.AddOrUpdate(key,
            _ => (link, messageAction, targets, Interlocked.Increment(ref _cacheOrderSequence)),
            (_, existing) => (link, messageAction, existing.Targets.Merge(targets), existing.Sequence));
    }

    private static string GetRelationshipKey(Link link)
    {
        return string.Join('|', link.Source?.Id, link.Source?.Property, link.Target?.Id, link.Target?.Property);
    }

    // Tracks unique entity identities so GetAssetCount() reports a current-state gauge, mirroring the stream/type caches.
    private void TrackEntityIdentity(string entityId, MessageAction messageAction)
    {
        if (messageAction == MessageAction.Delete)
        {
            _entityIds.TryRemove(entityId, out _);
        }
        else
        {
            _entityIds.TryAdd(entityId, 0);
        }
    }

    private string GetPrefixedOrSanitizedIdentifier(string id, Classification classification)
    {
        return classification == Classification.Static
            ? id.ToOmfIdentifier()
            : id.ToPrefixedOmfIdentifier(_streamIdPrefix);
    }

    private void CacheDataTypeUpdateInstrumentation(DataType dataType, MessageAction messageAction, PartitionTargets targets)
    {
        _dataTypes.AddOrUpdate(dataType.Id, _ =>
        {
            Interlocked.Increment(ref _typeCount);
            return (dataType, messageAction, targets, Interlocked.Increment(ref _cacheOrderSequence));
        }, (_, existing) => (dataType, messageAction, existing.Targets.Merge(targets), existing.Sequence));
    }

    private void CacheDataStreamUpdateInstrumentation(DataStream dataStream, MessageAction messageAction, PartitionTargets targets)
    {
        _dataStreams.AddOrUpdate(dataStream.Id, _ =>
        {
            Interlocked.Increment(ref _streamCount);
            return (dataStream, messageAction, targets, Interlocked.Increment(ref _cacheOrderSequence));
        }, (_, existing) => (dataStream, messageAction, existing.Targets.Merge(targets), existing.Sequence));
    }

    private void IncrementIoEventsCount()
    {
        Interlocked.Increment(ref _ioEventsCount);
    }

    private void AddToIoEventsCount(int count)
    {
        Interlocked.Add(ref _ioEventsCount, count);
    }

    private void AddMetadataValues(DataStream dataStream)
    {
        if (StreamMetadataLevel >= MetadataInfo.Low)
        {
            if (dataStream.Metadata == null)
            {
                dataStream.Metadata = _metaDataDictionary;
            }
            else if (!ReferenceEquals(dataStream.Metadata, _metaDataDictionary))
            {
                foreach (var (key, value) in _metaDataDictionary)
                {
                    dataStream.Metadata[key] = value;
                }
            }
        }
        else
        {   
            dataStream.Metadata = [];
        }
    }

    private string GetDataSource(string dataSource, string messageId)
    {
        if (!string.IsNullOrEmpty(dataSource) &&
            !string.Equals(dataSource, _componentId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Overwriting message '{ContainerId}' with {DataSourceToken} property '{DataSource}' to match Component Id '{ComponentId}'.",
                messageId, Tokens.DataSource, dataSource, _componentId);
        }

        return _componentId;
    }

    private void ExcludeStreamProperties(DataStream dataStream)
    {
        if (!IncludeSourceProperties.HasFlag(StreamProperties.Description))
        {
            dataStream.Description = null;
        }

        if (dataStream.PropertyOverrides == null)
        {
            return;
        }

        foreach (var streamPropertyOverride in dataStream.PropertyOverrides.Values)
        {
            if (streamPropertyOverride == null)
            {
                continue;
            }

            foreach (var prop in _propertyOverrideActions)
            {
                if (!IncludeSourceProperties.HasFlag(prop.Key))
                {
                    prop.Value.Invoke(streamPropertyOverride);
                }
            }
        }
    }

    #endregion

    #region Private Types

    // Keys are only ever added: schema must stay ahead of dependent instance data in every partition it has reached.
    // IncludeDefaultPartition is additive to Keys: it also sends once with no key, which the service routes by client identity.
    private readonly record struct PartitionTargets(PartitionKey[] Keys, bool IncludeDefaultPartition)
    {
        public static PartitionTargets From(PartitionKey? partitionKey)
        {
            return partitionKey.HasValue ? new([partitionKey.Value], false) : new([], true);
        }

        public PartitionTargets Merge(PartitionTargets other)
        {
            var keys = other.Keys.Length == 0 ? Keys : [.. Keys.Union(other.Keys).Order()];
            return new(keys, IncludeDefaultPartition || other.IncludeDefaultPartition);
        }
    }

    #endregion
}
