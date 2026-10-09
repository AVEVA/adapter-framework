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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFilters;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.MessageProcessor.DataFilters;
using AdapterFramework.Data.Framework.Messages.Awaitable;

namespace AdapterFramework.Data.Framework.MessageProcessor;

public class AdapterMessageProcessor : IAwaitableAdapterMessageProcessor
{
    #region Private Fields

    private const double PercentToDecimalConversion = 100.0;
    private readonly IScopedMessageProcessor _messageProcessor;
    private readonly ConcurrentDictionary<string, IDataFilter> _dataFilters;
    private readonly OmfVersion _omfVersion;

    #endregion

    #region Public Constructor

    public AdapterMessageProcessor(IMessageProcessor messageProcessor, OmfVersion omfVersion)
    {
        ThrowHelper.ThrowIfArgumentNull(messageProcessor, nameof(messageProcessor));

        _messageProcessor = messageProcessor.AsScoped();
        _dataFilters = new ConcurrentDictionary<string, IDataFilter>(StringComparer.OrdinalIgnoreCase);
        _omfVersion = omfVersion;
    }

    #endregion

    #region Public Methods

    public static void ThrowIfInvalidPartitionKey(PartitionKey? partitionKey)
    {
        if (partitionKey.HasValue && !Enum.IsDefined(partitionKey.Value))
        {
            var partitionKeyValues = Enum.GetValues<PartitionKey>();
            throw new ArgumentOutOfRangeException(nameof(partitionKey), partitionKey.Value, $"PartitionKey must be a defined value between {(byte)partitionKeyValues.First()} and {(byte)partitionKeyValues.Last()}.");
        }
    }

    /// <inheritdoc/>
    public OmfVersion OmfVersion => _omfVersion;

    /// <summary>
    /// Handles update to <see cref="DataFiltersConfiguration"/> pushed to the adapter.
    /// </summary>
    /// <param name="dataFiltersConfiguration">Updated <see cref="DataFiltersConfiguration"/> configuration.</param>
    public void ProcessDataFiltersConfigurationChanges(DataFiltersConfiguration[] dataFiltersConfiguration)
    {
        _dataFilters.Clear();

        if (dataFiltersConfiguration != null)
        {
            foreach (var filter in dataFiltersConfiguration)
            {
                IDataFilter dataFilter = null;
                if (filter.AbsoluteDeadband == 0 || filter.PercentChange == 0)
                {
                    dataFilter = new AbsoluteDeadbandDataFilter(filter.ExpirationPeriod, 0);
                }
                else if (filter.AbsoluteDeadband > 0)
                {
                    dataFilter = new AbsoluteDeadbandDataFilter(filter.ExpirationPeriod, (double)filter.AbsoluteDeadband);
                }
                else if (filter.PercentChange > 0)
                {
                    dataFilter = new PercentChangeDataFilter(filter.ExpirationPeriod, (double)filter.PercentChange / PercentToDecimalConversion);
                }

                _dataFilters.TryAdd(filter.Id, dataFilter);
            }
        }
    }

    /// <summary>
    /// Returns <see cref="IDataFilter"/> instance by ID.
    /// </summary>
    /// <param name="dataFilterId">ID of <see cref="IDataFilter"/> instance.</param>
    /// <returns><see cref="IDataFilter"/> instance or Null when not found.</returns>
    public IDataFilter LookupFilterById(string dataFilterId)
    {
        if (string.IsNullOrEmpty(dataFilterId))
        {
            return null;
        }

        _dataFilters.TryGetValue(dataFilterId, out var dataFilter);
        return dataFilter;
    }

    #endregion

    #region IAdapterMessageProcessor Implementation

    /// <inheritdoc/>
    public bool TryCreateAwaitableScope(OmfAwaitableScopeOptions options, out IAwaitableAdapterMessageScope scope)
    {
        if (!_messageProcessor.TryCreateScope(options, out var token))
        {
            scope = null;
            return false;
        }

        scope = new AwaitableAdapterMessageScope(this, token);
        return true;
    }

    /// <inheritdoc/>
    public void WriteType(DataType dataType, MessageAction messageAction) => WriteTypeCore(dataType, messageAction, null);

    /// <inheritdoc/>
    public void WriteTypes(DataType[] dataTypes, MessageAction messageAction) => WriteTypesCore(dataTypes, messageAction, null);

    /// <inheritdoc/>
    public void WriteStream(DataStream dataStream, MessageAction messageAction) => WriteStreamCore(dataStream, messageAction, null);

    /// <inheritdoc/>
    public void WriteStreams(DataStream[] dataStreams, MessageAction messageAction) => WriteStreamsCore(dataStreams, messageAction, null);

    /// <inheritdoc/>
    public virtual void WriteDynamicValue<T>(IDataSelectionConfiguration dataSelectionItem, T instance, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class =>
        WriteDynamicValueCore(dataSelectionItem, instance, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public virtual void WriteDynamicValues<T>(IDataSelectionConfiguration dataSelectionItem, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey = null) where T : class =>
        WriteDynamicValuesCore(dataSelectionItem, instances, messageAction, partitionKey, null);

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata = null,
        List<string> tags = null, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides = null,
        MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteStaticValueCore(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction, null);

    /// <inheritdoc/>
    public void WriteStaticValue<T>(string typeId, string id, string name, string description, string dataSource,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata = null,
        List<string> tags = null, List<Link> relationships = null, MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteStaticValueCore(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, null);

    public void WriteInstanceRelationship(Link link, MessageAction messageAction = MessageAction.Default) => WriteInstanceRelationshipCore(link, messageAction, null);

    public void WriteTypeRelationship(Link link, MessageAction messageAction = MessageAction.Default) => WriteTypeRelationshipCore(link, messageAction, null);

    public void WriteEvent<T>(string typeId, string id, string name, string description, DateTime startTime, DateTime? endTime, IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions,
        IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance, IReadOnlyDictionary<string, object> metadata = null, List<string> tags = null, List<Link> relationships = null,
        MessageAction messageAction = MessageAction.Default) where T : class =>
        WriteEventCore(typeId, id, name, description, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, null);

    #endregion

    #region Core Write Methods

    // Core methods take the scope, if any. Subclasses override these so scoped and unscoped writes behave the same.
    protected internal virtual void WriteTypeCore(DataType dataType, MessageAction messageAction, ScopeToken scope)
    {
        _messageProcessor.WriteType(dataType, messageAction, scope);
    }

    protected internal virtual void WriteTypesCore(DataType[] dataTypes, MessageAction messageAction, ScopeToken scope)
    {
        _messageProcessor.WriteTypes(dataTypes, messageAction, scope);
    }

    protected internal virtual void WriteStreamCore(DataStream dataStream, MessageAction messageAction, ScopeToken scope)
    {
        _messageProcessor.WriteStream(dataStream, messageAction, scope);
    }

    protected internal virtual void WriteStreamsCore(DataStream[] dataStreams, MessageAction messageAction, ScopeToken scope)
    {
        _messageProcessor.WriteStreams(dataStreams, messageAction, scope);
    }

    protected internal virtual void WriteDynamicValueCore<T>(IDataSelectionConfiguration dataSelectionItem, T instance, MessageAction messageAction, PartitionKey? partitionKey, ScopeToken scope)
        where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(dataSelectionItem, nameof(dataSelectionItem));
        ThrowHelper.ThrowIfArgumentNull(instance, nameof(instance));
        ThrowIfInvalidPartitionKey(partitionKey);

        var dataFilter = LookupFilterById(dataSelectionItem.DataFilterId);
        if (dataFilter == null)
        {
            _messageProcessor.WriteDynamicValue(dataSelectionItem.StreamId, instance, messageAction, partitionKey, scope);
            dataSelectionItem.DataFilterCache = null;
        }
        else
        {
            dataSelectionItem.DataFilterCache ??= new DataFilterCachedValues<T>();
            var typedCache = (DataFilterCachedValues<T>)dataSelectionItem.DataFilterCache;
            
            typedCache.SetCurrentValue(instance);

            if (dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out var sendPrevious))
            {
                if (sendPrevious)
                {
                    _messageProcessor.WriteDynamicValue(dataSelectionItem.StreamId, typedCache.GetPreviousValue(), messageAction, partitionKey, scope);
                    typedCache.SetPreviousValue(instance);
                }

                _messageProcessor.WriteDynamicValue(dataSelectionItem.StreamId, instance, messageAction, partitionKey, scope);
            }
        }
    }

    protected internal virtual void WriteDynamicValuesCore<T>(IDataSelectionConfiguration dataSelectionItem, IReadOnlyList<T> instances, MessageAction messageAction, PartitionKey? partitionKey,
        ScopeToken scope)
        where T : class
    {
        ThrowHelper.ThrowIfArgumentNull(dataSelectionItem, nameof(dataSelectionItem));
        ThrowHelper.ThrowIfArgumentNull(instances, nameof(instances));
        ThrowIfInvalidPartitionKey(partitionKey);

        var dataFilter = LookupFilterById(dataSelectionItem.DataFilterId);
        if (dataFilter == null)
        {
            _messageProcessor.WriteDynamicValues(dataSelectionItem.StreamId, instances, messageAction, partitionKey, scope);
            dataSelectionItem.DataFilterCache = null;
        }
        else
        {
            dataSelectionItem.DataFilterCache ??= new DataFilterCachedValues<T>();
            var typedCache = (DataFilterCachedValues<T>)dataSelectionItem.DataFilterCache;

            var instancesToWrite = new List<T>();
            foreach (var valueInstance in instances)
            {
                typedCache.SetCurrentValue(valueInstance);

                if (dataSelectionItem.DataFilterCache.CheckDataFilter(dataFilter, out var sendPrevious))
                {
                    if (sendPrevious)
                    {
                        instancesToWrite.Add(typedCache.GetPreviousValue());
                        typedCache.SetPreviousValue(valueInstance);
                    }

                    instancesToWrite.Add(valueInstance);
                }
            }

            if (instancesToWrite.Count > 0)
            {
                _messageProcessor.WriteDynamicValues(dataSelectionItem.StreamId, instancesToWrite, messageAction, partitionKey, scope);
            }
        }
    }

    protected internal virtual void WriteStaticValueCore<T>(string typeId, string id, string name, string description, string dataSource, T instance, IReadOnlyDictionary<string, object> metadata,
        List<string> tags, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, MessageAction messageAction, ScopeToken scope)
        where T : class
    {
        if (_omfVersion == OmfVersion.Omf12)
        {
            throw new NotSupportedException("Static values are not supported in OMF 1.2.");
        }

        if (messageAction != MessageAction.Delete && string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("typeId is required unless messageAction is Delete.", nameof(typeId));
        }

        _messageProcessor.WriteStaticValue(typeId, id, name, description, dataSource, instance, metadata, tags, propertyOverrides, messageAction, scope);
    }

    protected internal virtual void WriteStaticValueCore<T>(string typeId, string id, string name, string description, string dataSource,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides,
        T instance, IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction, ScopeToken scope)
        where T : class
    {
        if (_omfVersion == OmfVersion.Omf12)
        {
            throw new NotSupportedException("Static values are not supported in OMF 1.2.");
        }

        if (messageAction != MessageAction.Delete && string.IsNullOrWhiteSpace(typeId))
        {
            throw new ArgumentException("typeId is required unless messageAction is Delete.", nameof(typeId));
        }

        _messageProcessor.WriteStaticValue(typeId, id, name, description, dataSource, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, scope);
    }

    protected internal virtual void WriteInstanceRelationshipCore(Link link, MessageAction messageAction, ScopeToken scope)
    {
        if (_omfVersion == OmfVersion.Omf12)
        {
            throw new NotSupportedException("Instance relationships are not supported in OMF 1.2.");
        }

        _messageProcessor.WriteInstanceRelationship(link, messageAction, scope);
    }

    protected internal virtual void WriteTypeRelationshipCore(Link link, MessageAction messageAction, ScopeToken scope)
    {
        if (_omfVersion == OmfVersion.Omf12) 
        {
            throw new NotSupportedException("Type relationships are not supported in OMF 1.2.");
        }

        _messageProcessor.WriteSchemaRelationship(link, messageAction, scope);
    }

    protected internal virtual void WriteEventCore<T>(string typeId, string id, string name, string description, DateTime startTime, DateTime? endTime,
        IReadOnlyDictionary<string, PropertyDefinition> extendedPropertyDefinitions, IReadOnlyDictionary<string, PropertyDefinitionOverride> propertyOverrides, T instance,
        IReadOnlyDictionary<string, object> metadata, List<string> tags, List<Link> relationships, MessageAction messageAction, ScopeToken scope)
        where T : class
    {
        if (_omfVersion == OmfVersion.Omf12) 
        {
            throw new NotSupportedException("Events are not supported in OMF 1.2.");
        }

        _messageProcessor.WriteEvent(id, typeId, name, description, null, startTime, endTime, extendedPropertyDefinitions, propertyOverrides, instance, metadata, tags, relationships, messageAction, scope);
    }

    #endregion
}
