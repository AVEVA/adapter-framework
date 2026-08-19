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
using AdapterFramework.Data.Framework.Abstractions.Constants;

namespace AdapterFramework.Data.Framework.AdapterCommon;

public static class CommonConstants
{
    public const string DataSourceConfigurationName = "DataSource";
    public const string DataSelectionConfigurationName = "DataSelection";
    public const string SchedulesConfigurationName = "Schedules";
    public const string DataFiltersConfigurationName = "DataFilters";
    public const string DiscoveriesConfigurationName = "Discoveries";
    public const string HistoryRecoveryConfigurationName = "HistoryRecoveries";
    public const string IntervalsToRecoverConfigurationName = "IntervalsToRecover";

    public const string LoggingConfigurationName = EdgeSystemConstants.LoggingFacetName;
    public const string LogMessagePrefixKey = EdgeSystemConstants.LogMessagePrefixPlaceholder;

    public const string StartCallbackName = "Start";
    public const string StopCallbackName = "Stop";

    public const string GetStreamIdPatternKeywordsRegex = @"(?<=\{)[^}]*(?=\})";

    public const string TimeIndexedTypeIdPrefix = "TimeIndexed";
    public const string TimestampPropertyName = "Timestamp";
    public const string ValuePropertyName = "Value";
    public const string QualityPropertyName = "Quality";
    public const string DataQualitySchemaName = "DataQualitySchema";

    public const string NullableTypeName = "Nullable";
}
