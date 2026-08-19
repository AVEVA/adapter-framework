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

namespace AdapterFramework.Data.DataModel;

#pragma warning disable CA2227 // Collection properties should be read only. Justification: The collection properties need to be settable.
public sealed class SchemaMessageWrapper
{
    [System.Text.Json.Serialization.JsonPropertyName("types")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ArraySegment<DataType> Types { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("containers")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ArraySegment<DataStream> Streams { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("relationships")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public ArraySegment<Link> Relationships { get; set; }
}
#pragma warning restore CA2227 // Collection properties should be read only
