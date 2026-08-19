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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.DataModel.Enum;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.General;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;
using AdapterFramework.Data.Framework.Abstractions.Security;

namespace AdapterFramework.Data.Framework.Abstractions.Services;

/// <summary>
/// Defines set of services provided to <see cref="IEdgeAdapter"/> component.
/// </summary>
public interface IAdapterCommonService
{
    /// <summary>
    /// Logger instance <see cref="ILogger"/>.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Property determining what stream metadata verbosity level is enabled <see cref="MetadataInfo"/>.
    /// </summary>
    MetadataInfo StreamMetadataLevel { get; }

    /// <summary>
    /// Property determining what stream properties will get sent <see cref="StreamProperties"/>.
    /// </summary>
    StreamProperties IncludeSourceProperties { get; }

    /// <summary>
    /// Gets the final prefix applied to the stream Ids by the adapter framework.
    /// </summary>
    /// <value>
    /// Property <c>StreamIdPrefix</c> takes the value of <see cref="DataSourceConfigurationBase"/> property <c>StreamIdPrefix</c> when it is not null.
    /// Otherwise, it takes a value of adapter component Id appended with a separator '.'.
    /// </value>
    public string StreamIdPrefix { get; }

    /// <summary>
    /// Configuration provider instance <see cref="IComponentConfigurationProvider"/>.
    /// </summary>
    IComponentConfigurationProvider ConfigurationProvider { get; }

    /// <summary>
    /// Default stream ID generator instance <see cref="DefaultStreamIdGenerator"/>.
    /// </summary>
    IDefaultStreamIdGenerator DefaultStreamIdGenerator { get; }

    /// <summary>
    /// Message Processor instance <see cref="IAdapterMessageProcessor"/>.
    /// </summary>
    IAdapterMessageProcessor MessageProcessor { get; }

    /// <summary>
    /// Data protector instance <see cref="IEdgeDataProtector"/>.
    /// </summary>
    IEdgeDataProtector DataProtector { get; }

    /// <summary>
    /// Health service instance <see cref="IHealthService"/>.
    /// </summary>
    IHealthService HealthService { get; }

    /// <summary>
    /// Returns the adapter specific type Id for the given <paramref name="enumName"/>.
    /// </summary>
    /// <param name="enumName">The enumeration's name.</param>
    /// <returns>Adapter specific <see cref="DataType"/> ID."</returns>
    string GetEnumTypeId(string enumName);

    /// <summary>
    /// Returns <see cref="DataType"/> for the given enumeration.
    /// </summary>
    /// <param name="enumeration">The value field of the enum data type.</param>
    /// <param name="enumName">The name of the enum.</param>
    /// <returns>The <see cref="DataType"/> containing the enum definition.</returns>
    DataType GetEnumDataType(TypeEnumField enumeration, string enumName);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> ID for the given <paramref name="enumName"/>.
    /// </summary>
    /// <param name="enumName">The name of the enum (not the enum typeId).</param>
    /// <returns>TimeIndexed <see cref="DataType"/> ID.</returns>
    string GetTimeIndexedEnumDataTypeId(string enumName);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> ID for the given <paramref name="enumName"/> and <paramref name="qualitySchema"/>.
    /// </summary>
    /// <param name="enumName">The name of the enum (not the enum typeId).</param>
    /// <param name="qualitySchema">Data quality schema identifier.</param>
    /// <returns>TimeIndexed <see cref="DataType"/> ID.</returns>
    string GetTimeIndexedEnumDataTypeId(string enumName, string qualitySchema);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> for the given <paramref name="enumName"/>.
    /// </summary>
    /// <param name="enumName">The name of the enum (not the enum typeId).</param>
    /// <returns>Common <see cref="DataType"/> instance with Timestamp and Value properties.</returns>
    DataType GetTimeIndexedEnumDataType(string enumName);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> for the given <paramref name="enumName"/>, <paramref name="qualityType"/> and <paramref name="qualitySchema"/>.
    /// </summary>
    /// <param name="enumName">The name of the enum (not the enum typeId).</param>
    /// <param name="qualityType">Type of the Quality property.</param>
    /// <param name="qualitySchema">Data quality schema identifier.</param>
    /// <returns>Common <see cref="DataType"/> instance with Timestamp, Quality and Value properties.</returns>
    DataType GetTimeIndexedEnumDataType(string enumName, Type qualityType, string qualitySchema);

    /// <summary>
    /// Returns the adapter specific type Id for the given <paramref name="typeName"/>.
    /// </summary>
    /// <param name="typeName">The type's name, for example: WeeklySchedule, PumpType.</param>
    /// <returns>Adapter specific <see cref="DataType"/> ID.</returns>
    string GetAdapterDataTypeId(string typeName);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> ID for the given <paramref name="valueType"/>.
    /// </summary>
    /// <param name="valueType">Type if the Value property.</param>
    /// <returns>TimeIndexed <see cref="DataType"/> ID.</returns>
    /// <remarks>Exception <see cref="NotSupportedException"/> will be thrown when unsupported <paramref name="valueType"/> is passed.</remarks>
    string GetTimeIndexedDataTypeId(Type valueType);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> for the given <paramref name="valueType"/>.
    /// </summary>
    /// <param name="valueType">Type of the Value property.</param>
    /// <returns>Common <see cref="DataType"/> instance with Timestamp and Value properties.</returns>
    /// <remarks>Exception <see cref="NotSupportedException"/> will be thrown when unsupported <paramref name="valueType"/> is passed.</remarks>
    DataType GetTimeIndexedDataType(Type valueType);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> ID for the given <paramref name="valueType"/>, and <paramref name="qualitySchema"/>.
    /// </summary>
    /// <param name="valueType">Type of the Value property.</param>
    /// <param name="qualitySchema">Data quality schema identifier.</param>
    /// <returns>TimeIndexed <see cref="DataType"/> ID.</returns>
    /// <remarks>Exception <see cref="NotSupportedException"/> will be thrown when unsupported <paramref name="valueType"/> is passed.</remarks>
    string GetTimeIndexedDataTypeId(Type valueType, string qualitySchema);

    /// <summary>
    /// Returns TimeIndexed <see cref="DataType"/> for the given <paramref name="valueType"/>, <paramref name="qualityType"/>, and <paramref name="qualitySchema"/>.
    /// </summary>
    /// <param name="valueType">Type of the Value property.</param>
    /// <param name="qualityType">Type of the Quality property.</param>
    /// <param name="qualitySchema">Data quality schema identifier.</param>
    /// <returns>Common <see cref="DataType"/> instance with Timestamp, Quality and Value properties.</returns>
    /// <remarks>Exception <see cref="NotSupportedException"/> will be thrown when unsupported <paramref name="valueType"/> is passed.</remarks>
    DataType GetTimeIndexedDataType(Type valueType, Type qualityType, string qualitySchema);
}
