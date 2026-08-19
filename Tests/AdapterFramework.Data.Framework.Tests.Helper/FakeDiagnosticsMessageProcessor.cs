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
using System.Collections.Generic;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Metadata;

namespace AdapterFramework.Data.Framework.Tests.Helper;

public class FakeDiagnosticsMessageProcessor : IDiagnosticsMessageProcessor
{
    private readonly List<DataType> _omfTypes;
    private readonly List<DataStream> _omfContainers;
    private readonly List<string> _omfData;

    public FakeDiagnosticsMessageProcessor(List<DataType> omfTypes, List<DataStream> omfContainers, List<string> omfData, string dataStreamIdPrefix = "")
    {
        _omfTypes = omfTypes;
        _omfContainers = omfContainers;
        _omfData = omfData;

        StreamIdPrefix = dataStreamIdPrefix;
    }

    public FakeDiagnosticsMessageProcessor()
    {
        _omfTypes = new List<DataType>();
        _omfContainers = new List<DataStream>();
        _omfData = new List<string>();
    }

    public MetadataInfo StreamMetadataLevel { get; set; }

    public string StreamIdPrefix { get; }

    public bool SystemDiagnosticsEnabled { get; set; }

    public void WriteDiagnosticsStreams(DataStream[] dataStreams)
    {
        _omfContainers.AddRange(dataStreams);
    }

    public void WriteDiagnosticsValue<T>(string id, Classification classification, T instance)
    {
        _omfData.Add(id);
    }

    public void WriteDiagnosticsTypes(DataType[] dataTypes)
    {
        _omfTypes.AddRange(dataTypes);
    }
}
