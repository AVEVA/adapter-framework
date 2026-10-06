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
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.MessageProcessor;

public class InstrumentedMessageProcessor : IInstrumentedMessageProcessor
{
    #region Private Fields

    private readonly IMessageProcessor _messageProcessor;
    private readonly ConcurrentDictionary<string, (DataType DataType, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> _dataTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DataStream DataStream, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> _dataStreams = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (Link Link, MessageAction MessageAction, PartitionTargets Targets, long Sequence)> _relationships = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConditionalWeakTable<DataStream, string> _preparedStreamIds = [];
    private readonly Dictionary<string, object> _metaDataDictionary;
    private readonly Dictionary<StreamProperties, Action<PropertyDefinitionOverride>> _propertyOverrideActions;
    private readonly string _componentId;
    private readonly ILogger _logger;

    private string _streamIdPrefix;
    private int _streamCount;
    private int _typeCount;
    private long _eventsCount;
    private long _cacheOrderSequence;

    #endregion

    #region Public Constructor

    public InstrumentedMessageProcessor(IMessageProcessor messageProcessor, ILogger logger, string componentId, string componentType)
    {
        _messageProcessor = messageProcessor;
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
    public void WriteType(DataType dataType, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataType, nameof(dataType));

        PrepareAndCacheDataType(dataType, messageAction, null);

        _messageProcessor.WriteType(dataType, messageAction);
    }

    /// <inheritdoc/>
    public void WriteType(DataType dataType, PartitionKey partitionKey, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataType, nameof(dataType));

        PrepareAndCacheDataType(dataType, messageAction, partitionKey);

        _messageProcessor.WriteType(dataType, partitionKey, messageAction);
    }

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));

        foreach (var dataType in dataTypes)
        {
            PrepareAndCacheDataType(dataType, messageAction, null);
        }

        _messageProcessor.WriteTypes(dataTypes, messageAction);
    }

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, PartitionKey partitionKey, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataTypes, nameof(dataTypes));

        foreach (var dataType in dataTypes)
        {
            PrepareAndCacheDataType(dataType, messageAction, partitionKey);
        }

        _messageProcessor.WriteTypes(dataTypes, partitionKey, messageAction);
    }

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

        PrepareAndCacheDataStream(dataStream, messageAction, null);

        _messageProcessor.WriteStream(dataStream, messageAction);
    }

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, PartitionKey partitionKey, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

        PrepareAndCacheDataStream(dataStream, messageAction, partitionKey);

        _messageProcessor.WriteStream(dataStream, partitionKey, messageAction);
    }

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));

        foreach (var dataStream in dataStreams)
        {
            ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

            PrepareAndCacheDataStream(dataStream, messageAction, null);
        }

        _messageProcessor.WriteStreams(dataStreams, messageAction);
    }

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, PartitionKey partitionKey, MessageAction messageAction)
    {
        ThrowHelper.ThrowIfArgumentNull(dataStreams, nameof(dataStreams));

        foreach (var dataStream in dataStreams)
        {
            ThrowHelper.ThrowIfArgumentNull(dataStream, nameof(dataStream));

            PrepareAndCacheDataStream(dataStream, messageAction, partitionKey);
        }

        _messageProcessor.WriteStreams(dataStreams, partitionKey, messageAction);
    }

    /// <inheritdoc/>
    public void WriteValue<T>(string id, Classification classification, T instance, MessageAction messageAction) where T : class
    {
        _messageProcessor.WriteValue(GetPrefixedOrSanitizedIdentifier(id, classification), classification, instance, messageAction);

        IncrementEventsCount();
    }

    /// <inheritdoc/>
    public void WriteDynamicValue<T>(string id, T instance, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class
    {
        _messageProcessor.WriteDynamicValue(GetPrefixedOrSanitizedIdentifier(id, Classification.Dynamic), instance, messageAction, partitionKey);

        IncrementEventsCount();
    }

    /// <inheritdoc/>
    public void WriteValues<T>(string id, Classification classification, IReadOnlyList<T> instances, MessageAction messageAction) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        _messageProcessor.WriteValues(GetPrefixedOrSanitizedIdentifier(id, classification), classification, instances, messageAction);

        AddToEventsCount(instances.Count);
    }

    /// <inheritdoc/>
    public void WriteDynamicValues<T>(string id, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));

        _messageProcessor.WriteDynamicValues(GetPrefixedOrSanitizedIdentifier(id, Classification.Dynamic), instances, messageAction, partitionKey);

        AddToEventsCount(instances.Count);
    }

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string id, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, MessageAction messageAction) where T : class
    {
        _messageProcessor.WriteStaticValue(id.ToOmfIdentifier(), extendedPropertyDefinitions, propertyOverrides, instance, metadata, messageAction);

        IncrementEventsCount();
    }

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class
    {
        _messageProcessor.WriteStaticValue(ToOmfTypeIdOrNull(typeId, messageAction), id.ToOmfIdentifier(), name, description, GetDataSource(dataSource, id), extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction);

        IncrementEventsCount();
    }

    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags = null,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null, MessageAction messageAction = MessageAction.Default) where T : class
    {
        _messageProcessor.WriteStaticValue(ToOmfTypeIdOrNull(typeId, messageAction), id.ToOmfIdentifier(), name, description, GetDataSource(dataSource, id), instance, metadata, tags, propertyOverrides, messageAction);

        IncrementEventsCount();
    }

    public void WriteEvent<T>(string id, string typeId, string name, string description, string dataSource, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class
    {
        _messageProcessor.WriteEvent(id.ToOmfIdentifier(), typeId.ToOmfIdentifier(), name, description, GetDataSource(dataSource, id), startTime, endTime,
            extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction);

        IncrementEventsCount();
    }

    public void WriteSchemaRelationship(Link link, MessageAction messageAction = MessageAction.Default)
    {
        ThrowHelper.ThrowIfArgumentNull(link, nameof(link));

        PrepareAndCacheRelationship(link, messageAction, null);

        _messageProcessor.WriteSchemaRelationship(link, messageAction);
    }

    /// <inheritdoc/>
    public void WriteSchemaRelationship(Link link, PartitionKey partitionKey, MessageAction messageAction = MessageAction.Default)
    {
        ThrowHelper.ThrowIfArgumentNull(link, nameof(link));

        PrepareAndCacheRelationship(link, messageAction, partitionKey);

        _messageProcessor.WriteSchemaRelationship(link, partitionKey, messageAction);
    }

    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default)
    {
        _messageProcessor.WriteInstanceRelationship(link, messageAction);
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
    public long GetAndResetEventsCounter()
    {
        return Interlocked.Exchange(ref _eventsCount, 0);
    }

    /// <inheritdoc/>
    public void ClearCounters()
    {
        Interlocked.Exchange(ref _typeCount, 0);
        Interlocked.Exchange(ref _streamCount, 0);
        Interlocked.Exchange(ref _eventsCount, 0);
    }

    #endregion

    /// <inheritdoc/>
    public void ResendTypesAndStreams()
    {
        Resend(_dataTypes.Values, _messageProcessor.WriteType, _messageProcessor.WriteType);
        Resend(_dataStreams.Values, _messageProcessor.WriteStream, _messageProcessor.WriteStream);
        Resend(_relationships.Values, _messageProcessor.WriteSchemaRelationship, _messageProcessor.WriteSchemaRelationship);
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

    private void PrepareAndCacheDataType(DataType dataType, MessageAction messageAction, PartitionKey? partitionKey)
    {
        dataType.Id = dataType.Id.ToOmfIdentifier();

        EncodeUnsupportedCharactersInReferences(dataType);

        CacheDataTypeUpdateInstrumentation(dataType, messageAction, PartitionTargets.From(partitionKey));
    }

    private void PrepareAndCacheDataStream(DataStream dataStream, MessageAction messageAction, PartitionKey? partitionKey)
    {
        // Callers rewrite the same object once per partition key; prefixing it again would change its ID.
        if (!_preparedStreamIds.TryGetValue(dataStream, out var preparedId) || !string.Equals(preparedId, dataStream.Id, StringComparison.Ordinal))
        {
            dataStream.Id = dataStream.Id.ToPrefixedOmfIdentifier(_streamIdPrefix);
            _preparedStreamIds.AddOrUpdate(dataStream, dataStream.Id);
        }

        dataStream.TypeId = dataStream.TypeId?.ToOmfIdentifier();

        AddMetadataValues(dataStream);

        dataStream.DataSource = GetDataSource(dataStream.DataSource, dataStream.Id);
        ExcludeStreamProperties(dataStream);
        CacheDataStreamUpdateInstrumentation(dataStream, messageAction, PartitionTargets.From(partitionKey));
    }

    private void PrepareAndCacheRelationship(Link link, MessageAction messageAction, PartitionKey? partitionKey)
    {
        var key = GetRelationshipKey(link);
        var targets = PartitionTargets.From(partitionKey);

        _relationships.AddOrUpdate(key,
            _ => (link, messageAction, targets, Interlocked.Increment(ref _cacheOrderSequence)),
            (_, existing) => (link, messageAction, existing.Targets.Merge(targets), existing.Sequence));
    }

    private static string GetRelationshipKey(Link link)
    {
        return string.Join('|', link.Source?.Id, link.Source?.Property, link.Target?.Id, link.Target?.Property);
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

    private void IncrementEventsCount()
    {
        Interlocked.Increment(ref _eventsCount);
    }

    private void AddToEventsCount(int count)
    {
        Interlocked.Add(ref _eventsCount, count);
    }

    private void AddMetadataValues(DataStream dataStream)
    {
        if (StreamMetadataLevel >= MetadataInfo.Low)
        {
            if (dataStream.Metadata == null)
            {
                dataStream.Metadata = new Dictionary<string, object>(_metaDataDictionary);
            }
            else
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
