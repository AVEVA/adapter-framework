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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Common.Helpers;

namespace AdapterFramework.Data.Framework.AdapterCommon;

public class AdapterCmdHelpService : CmdHelpServiceBase
{
    public AdapterCmdHelpService(string componentId)
        : base(componentId)
    {
    }

    public static string GetConfigurationCommandBaseString()
    {
        return GetConfigurationCommandBase();
    }

    public string GetHelpHeaderString(string facet)
    {
        return GetConfigHelpHeader(facet);
    }

    public string GetLoggingHelpOutput()
    {
        return GetLoggingHelp();
    }

    public string GetSchedulesHelpOutput()
    {
        return GetConfigHelpHeader(CommonConstants.SchedulesConfigurationName) +
        $@"
{nameof(ScheduleConfiguration.Id)}               [Required] Unique identifier of the schedule.
{nameof(ScheduleConfiguration.Period)}           [Required] The data sampling rate of the schedule. Expected format is HH:MM:SS.###. 
{nameof(ScheduleConfiguration.Offset)}           [Optional] The offset from the midnight when the schedule starts. Expected format is HH:MM:SS.###. 
";
    }

    public string GetDataFiltersHelpInfo()
    {
        return GetConfigHelpHeader(CommonConstants.DataFiltersConfigurationName) +
        $@"
{nameof(DataFiltersConfiguration.Id)}               [Required] Unique identifier of the DataFilter.
{nameof(DataFiltersConfiguration.AbsoluteDeadband)} [Optional] Specifies the absolute change in data value that should cause the current value to pass the filter test. At least one of `AbsoluteDeadband` or `PercentChange` must be specified. 
{nameof(DataFiltersConfiguration.PercentChange)}    [Optional] Specifies the percent change from previous value that should cause the current value to pass the filter test. At least one of `AbsoluteDeadband` or `PercentChange` must be specified. 
{nameof(DataFiltersConfiguration.ExpirationPeriod)} [Optional] The length in time that can elapse after an event before automatically storing the next event. The expected format is HH:MM:SS.###. 
";
    }
}
