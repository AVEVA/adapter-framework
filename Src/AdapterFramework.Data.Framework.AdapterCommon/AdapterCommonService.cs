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
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.DataModel.Enum;
using AdapterFramework.Data.DataModel.Extensions;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.AdapterCommon.CommonConstants;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.AdapterCommon.Tests")]
namespace AdapterFramework.Data.Framework.AdapterCommon;

public class AdapterCommonService : IAdapterCommonService
{
    private const string Dot = ".";
    private const string SystemNamespaceName = "System";
    private const string EnumSuffix = "Enum";

    private readonly Type _charType = typeof(char);
    private readonly Type _objectType = typeof(object);
    private readonly DefaultStreamIdGenerator _defaultStreamIdGenerator;
    private readonly string _adapterType;
    private readonly string _adapterId;

    public AdapterCommonService(ILogger logger, IConfigurationProvider configurationProvider, IAdapterMessageProcessor messageProcessor,
        IEdgeDataProtector edgeDataProtector, string adapterType, string adapterId, IHealthService healthService)
    {
        Logger = logger;
        _adapterType = adapterType;
        _adapterId = adapterId;
        ConfigurationProvider = new ComponentConfigurationProvider(configurationProvider, adapterId, logger);
        _defaultStreamIdGenerator = new DefaultStreamIdGenerator();
        MessageProcessor = messageProcessor;
        DataProtector = edgeDataProtector;
        HealthService = healthService;
    }

    /// <inheritdoc/>
    public ILogger Logger { get; }

    /// <inheritdoc/>
    public IComponentConfigurationProvider ConfigurationProvider { get; }

    /// <inheritdoc/>
    public IDefaultStreamIdGenerator DefaultStreamIdGenerator { get => _defaultStreamIdGenerator; }

    /// <inheritdoc/>
    public IAdapterMessageProcessor MessageProcessor { get; }

    /// <inheritdoc/>
    public IEdgeDataProtector DataProtector { get; }

    /// <inheritdoc/>
    public IHealthService HealthService { get; }

    /// <inheritdoc/>
    public string StreamIdPrefix { get; private set; } = string.Empty;

    public string DefaultStreamIdPattern { get; private set; }

    /// <inheritdoc/>
    public MetadataInfo StreamMetadataLevel { get; set; }

    /// <inheritdoc/>
    public StreamProperties IncludeSourceProperties { get; set; }

    /// <inheritdoc/>
    public string GetAdapterDataTypeId(string typeName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(typeName, nameof(typeName));

        return _adapterType + Dot + typeName;
    }

    /// <inheritdoc/>
    public string GetEnumTypeId(string enumName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(enumName, nameof(enumName));

        if (enumName.EndsWith(EnumSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return _adapterType + Dot + enumName;
        }

        return _adapterType + Dot + enumName + EnumSuffix;
    }

    /// <inheritdoc/>
    public DataType GetEnumDataType(TypeEnumField enumeration, string enumName)
    {
        ThrowHelper.ThrowIfArgumentNull(enumeration, nameof(enumeration));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(enumName, nameof(enumName));

        return new EnumDataType(GetEnumTypeId(enumName), enumeration, enumName);
    }

    /// <inheritdoc/>
    public string GetTimeIndexedEnumDataTypeId(string enumName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(enumName, nameof(enumName));

        return TimeIndexedTypeIdPrefix + Dot + GetEnumTypeId(enumName);
    }

    /// <inheritdoc/>
    public string GetTimeIndexedEnumDataTypeId(string enumName, string qualitySchema)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(enumName, nameof(enumName));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(qualitySchema, nameof(qualitySchema));

        return TimeIndexedTypeIdPrefix + Dot + GetEnumTypeId(enumName) + Dot + qualitySchema + QualityPropertyName;
    }

    /// <inheritdoc/>
    public DataType GetTimeIndexedEnumDataType(string enumName)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(enumName, nameof(enumName));

        var timeStampProperty = typeof(DateTime).ToPropertyDefinition();
        timeStampProperty.IsIndex = true;

        var properties = new Dictionary<string, PropertyDefinition>
        {
            [TimestampPropertyName] = timeStampProperty,
            [ValuePropertyName] = new PropertyDefinition { RefTypeId = GetEnumTypeId(enumName) },
        };

        return new DynamicDataType(GetTimeIndexedEnumDataTypeId(enumName), enumName, properties, MessageProcessor.OmfVersion);
    }

    /// <inheritdoc/>
    public DataType GetTimeIndexedEnumDataType(string enumName, Type qualityType, string qualitySchema)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(enumName, nameof(enumName));
        ThrowHelper.ThrowIfArgumentNull(qualityType, nameof(qualityType));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(qualitySchema, nameof(qualitySchema));

        return GetEnumDataType(GetTimeIndexedEnumDataTypeId(enumName, qualitySchema), enumName, GetEnumTypeId(enumName), qualityType, qualitySchema);
    }

    /// <inheritdoc/>
    public string GetTimeIndexedDataTypeId(Type valueType)
    {
        ThrowHelper.ThrowIfArgumentNull(valueType, nameof(valueType));
        ThrowIfUnsupportedType(valueType);

        return TimeIndexedTypeIdPrefix + Dot + GetTypeName(valueType);
    }

    /// <inheritdoc/>
    public DataType GetTimeIndexedDataType(Type valueType)
    {
        ThrowHelper.ThrowIfArgumentNull(valueType, nameof(valueType));

        var properties = new Dictionary<string, (Type Type, bool IsIndex, bool IsQuality, string QualitySchema)>
        {
            [TimestampPropertyName] = (typeof(DateTime), true, false, null),
            [ValuePropertyName] = (valueType, false, false, null),
        };

        return new DynamicDataType(GetTimeIndexedDataTypeId(valueType), GetTypeName(valueType), properties, MessageProcessor.OmfVersion);
    }

    /// <inheritdoc/>
    public string GetTimeIndexedDataTypeId(Type valueType, string qualitySchema)
    {
        ThrowHelper.ThrowIfArgumentNull(valueType, nameof(valueType));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(qualitySchema, nameof(qualitySchema));
        ThrowIfUnsupportedType(valueType);

        return TimeIndexedTypeIdPrefix + Dot + GetTypeName(valueType) + Dot + qualitySchema + QualityPropertyName;
    }

    /// <inheritdoc/>
    public DataType GetTimeIndexedDataType(Type valueType, Type qualityType, string qualitySchema)
    {
        ThrowHelper.ThrowIfArgumentNull(valueType, nameof(valueType));
        ThrowHelper.ThrowIfArgumentNull(qualityType, nameof(qualityType));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(qualitySchema, nameof(qualitySchema));

        var properties = new Dictionary<string, (Type Type, bool IsIndex, bool IsQuality, string QualitySchema)>
        {
            [TimestampPropertyName] = (typeof(DateTime), true, false, null),
            [QualityPropertyName] = (qualityType, false, true, qualitySchema),
            [ValuePropertyName] = (valueType, false, false, null),
        };

        return new DynamicDataType(GetTimeIndexedDataTypeId(valueType, qualitySchema), GetTypeName(valueType), properties, MessageProcessor.OmfVersion)
        {
            Metadata = CreateQualityMetadataDictionary(qualitySchema),
        };
    }

    internal static string GetTypeName(Type type)
    {
        var typeName = type.Name;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            typeName = NullableTypeName + Nullable.GetUnderlyingType(type).Name;
        }

        return typeName;
    }

    internal void DataSourceHandler(IDataSourceConfiguration dataSourceConfiguration)
    {
        SetStreamIdPrefix(dataSourceConfiguration);
        SetDefaultStreamIdPattern(dataSourceConfiguration);

        _defaultStreamIdGenerator?.UpdateDefaultStreamIdPattern(DefaultStreamIdPattern);
    }

    internal void SetStreamIdPrefix(IDataSourceConfiguration dataSourceConfiguration)
    {
        if (dataSourceConfiguration == null)
        {
            return;
        }

        if (dataSourceConfiguration.StreamIdPrefix == null)
        {
            StreamIdPrefix = _adapterId + Dot;
        }
        else if (string.IsNullOrWhiteSpace(dataSourceConfiguration.StreamIdPrefix))
        {
            StreamIdPrefix = string.Empty;
        }
        else
        {
            StreamIdPrefix = dataSourceConfiguration.StreamIdPrefix;
        }
    }

    private DataType GetEnumDataType(string typeId, string typeName, string refTypeId, Type qualityType, string qualitySchema)
    {
        var timeStampProperty = typeof(DateTime).ToPropertyDefinition();
        timeStampProperty.IsIndex = true;

        var qualityProperty = qualityType.ToPropertyDefinition();

        if (MessageProcessor.OmfVersion == OmfVersion.Omf12)
        {
            qualityProperty.IsQuality = true;
        }
        else
        {
            qualityProperty.QualitySchema = qualitySchema;
        }

        var properties = new Dictionary<string, PropertyDefinition>
        {
            [TimestampPropertyName] = timeStampProperty,
            [QualityPropertyName] = qualityProperty,
            [ValuePropertyName] = new PropertyDefinition { RefTypeId = refTypeId },
        };

        return new DynamicDataType(typeId, typeName, properties, MessageProcessor.OmfVersion)
        {
            Metadata = CreateQualityMetadataDictionary(qualitySchema),
        };
    }

    private Dictionary<string, object> CreateQualityMetadataDictionary(string qualitySchema)
    {
        if (MessageProcessor.OmfVersion != OmfVersion.Omf12)
        {
            return null;
        }

        return new Dictionary<string, object>
        {
            [DataQualitySchemaName] = qualitySchema,
        };
    }

    private void ThrowIfUnsupportedType(Type valueType)
    {
        if (valueType.Namespace != SystemNamespaceName || valueType.IsArray || valueType == _charType || valueType == _objectType)
        {
            throw new NotSupportedException($"Unsupported {TimeIndexedTypeIdPrefix} property type received {valueType}.");
        }
    }

    private void SetDefaultStreamIdPattern(IDataSourceConfiguration dataSourceConfiguration)
    {
        if (dataSourceConfiguration == null)
        {
            return;
        }

        DefaultStreamIdPattern = dataSourceConfiguration.DefaultStreamIdPattern;
    }
}
