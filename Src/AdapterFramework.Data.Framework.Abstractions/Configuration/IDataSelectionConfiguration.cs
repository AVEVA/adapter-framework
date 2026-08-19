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
using System.Text.Json.Serialization;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Abstractions.DataFilters;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

public interface IDataSelectionConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether the item is selected for data acquisition.
    /// </summary>
    bool Selected { get; set; }

    /// <summary>
    /// The optional stream name (friendly name) of the data item collected from the data source. 
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The stream ID that will be used to create the stream.
    /// </summary>
    [Id]
    string StreamId { get; set; }

    /// <summary>
    /// The optional id of a <see cref="DataFiltersConfiguration"/>
    /// </summary>
    string DataFilterId { get; }

    /// <summary>
    /// Cache used by the Framework to support data filtering. This cache is used to store previous value and last value.
    /// Adapter developer does not need to do work to support usage of the cache. The framework handles allocating the object and storing the data.
    /// </summary>
    [JsonIgnore]
    IDataFilterCache DataFilterCache { get; set; }
}
