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
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.AdapterCommon.Diagnostics;
using AdapterFramework.Data.Framework.Common.Constants;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Diagnostics;

public class AdapterDiagnosticsOmfMessageCreator_Tests
{
    private const string UnitTestComponentId = "UnitTestComponentId";
    private readonly AdapterDiagnosticsOmfMessageCreator _messageCreator;
    private readonly FakeDiagnosticsMessageProcessor _messageProcessor;
    private readonly List<DataType> _dataTypes;
    private readonly List<DataStream> _dataStreams;
    private readonly List<string> _data;
    private readonly string _streamIdPrefix;

    public AdapterDiagnosticsOmfMessageCreator_Tests()
    {
        _dataTypes = new List<DataType>();
        _dataStreams = new List<DataStream>();
        _data = new List<string>();
        _messageProcessor = new FakeDiagnosticsMessageProcessor(_dataTypes, _dataStreams, _data);
        _streamIdPrefix = "MyMachine.MyService.";

        _messageCreator = new AdapterDiagnosticsOmfMessageCreator(UnitTestComponentId, _streamIdPrefix, new DataTypeLinkNode("a", "b"), OmfVersion.Omf20);
    }

    [Fact]
    public void AdapterDiagnosticsOmfMessageCreator_Constructor_InvalidInput_Test()
    {
        var linkNode = new DataTypeLinkNode("a", "b");
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsOmfMessageCreator(null, _streamIdPrefix, linkNode, OmfVersion.Omf20));
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsOmfMessageCreator(string.Empty, _streamIdPrefix, null, OmfVersion.Omf20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdapterDiagnosticsOmfMessageCreator(string.Empty, _streamIdPrefix, linkNode, OmfVersion.Omf20));
        Assert.Throws<ArgumentException>(() => new AdapterDiagnosticsOmfMessageCreator("  ", _streamIdPrefix, linkNode, OmfVersion.Omf20));
    }

    [Fact]
    public void AdapterDiagnosticsOmfMessageCreator_CreateAndSendStructure_Test()
    {
        const string UnitTestComponentType = "UnitTestComponentType";
        const int ExpectedMetadataItemCount = 2;
        const string DataSourceKey = "DataSource";
        const string AdapterTypeKey = "AdapterType";

        _messageCreator.CreateAndSendStructure(_messageProcessor, UnitTestComponentId, UnitTestComponentType);

        Assert.NotEmpty(_dataTypes);
        Assert.NotEmpty(_dataStreams);
        Assert.NotEmpty(_data);

        foreach (var dataStream in _dataStreams)
        {
            Assert.NotEmpty(dataStream.Metadata);
            Assert.NotNull(dataStream.Name);
            Assert.Equal(dataStream.Id.Substring(dataStream.Id.LastIndexOf('.') + 1), dataStream.Name);
            Assert.Equal(ExpectedMetadataItemCount, dataStream.Metadata.Count);
            Assert.Equal(UnitTestComponentId, dataStream.Metadata[DataSourceKey].ToString());
            Assert.Equal(UnitTestComponentType, dataStream.Metadata[AdapterTypeKey].ToString());
        }
    }

    [Fact]
    public void AdapterDiagnosticsOmfMessageCreator_GetErrorRateStreamId_Test()
    {
        Assert.Equal($"{_streamIdPrefix}{UnitTestComponentId}.{DiagnosticsConstants.ErrorRateStreamName}", _messageCreator.GetErrorRateStreamId());
    }

    [Fact]
    public void AdapterDiagnosticsOmfMessageCreator_GetIoRateStreamId_Test()
    {
        Assert.Equal($"{_streamIdPrefix}{UnitTestComponentId}.{DiagnosticsConstants.IoRateStreamName}", _messageCreator.GetIoRateStreamId());
    }

    [Fact]
    public void AdapterDiagnosticsOmfMessageCreator_GetStreamCountStreamId_Test()
    {
        Assert.Equal($"{_streamIdPrefix}{UnitTestComponentId}.{DiagnosticsConstants.StreamCountStreamName}", _messageCreator.GetStreamCountStreamId());
    }

    [Fact]
    public void AdapterDiagnosticsOmfMessageCreator_GetEventWriteCountStreamId_Test()
    {
        Assert.Equal($"{_streamIdPrefix}{UnitTestComponentId}.{DiagnosticsConstants.EventWriteCountStreamName}", _messageCreator.GetEventWriteCountStreamId());
    }

    [Theory]
    [InlineData(OmfVersion.Omf12, 3)]
    [InlineData(OmfVersion.Omf13, 3)]
    [InlineData(OmfVersion.Omf20, 5)]
    public void AdapterDiagnosticsOmfMessageCreator_CreateAndSendStructure_OmfVersionGatesAssetAndEventWriteCount_Test(OmfVersion omfVersion, int expectedCount)
    {
        const string UnitTestComponentType = "UnitTestComponentType";

        var messageCreator = new AdapterDiagnosticsOmfMessageCreator(UnitTestComponentId, _streamIdPrefix, new DataTypeLinkNode("a", "b"), omfVersion);

        messageCreator.CreateAndSendStructure(_messageProcessor, UnitTestComponentId, UnitTestComponentType);

        Assert.Equal(expectedCount, _dataTypes.Count);
        Assert.Equal(expectedCount, _dataStreams.Count);

        var isOmf20 = omfVersion == OmfVersion.Omf20;
        Assert.Equal(isOmf20, _dataStreams.Exists(x => x.Id.EndsWith($".{DiagnosticsConstants.AssetCountStreamName}", StringComparison.Ordinal)));
        Assert.Equal(isOmf20, _dataStreams.Exists(x => x.Id.EndsWith($".{DiagnosticsConstants.EventWriteCountStreamName}", StringComparison.Ordinal)));
    }
}
