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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.AdapterCommon.Diagnostics;
using AdapterFramework.Data.Framework.Tests.Helper;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Diagnostics;

public class AdapterDiagnosticsService_Tests
{
    private const int ExpectedTypeCount = 3;
    private const int ExpectedContainerCount = 3;
    private const int DiagnosticsShutdownDelayMsecs = 1000;
    private const int DiagnosticsMessagesRunoutDelayMSecs = 2000;
    private const string UnitTestComponentId = "UnitTest";
    private const string UnitTestComponentType = "UnitTestComponent";

    private readonly List<DataType> _diagnosticTypes;
    private readonly List<DataStream> _diagnosticContainers;
    private readonly List<string> _diagnosticData;
    private readonly IDiagnosticsMessageProcessor _fakeDiagnosticsMessageProcessor;
    private readonly LinkNode _elementNode = new DataTypeLinkNode("abc", "def");

    public AdapterDiagnosticsService_Tests()
    {
        var instrumentedMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        instrumentedMessageProcessor.Setup(iom => iom.GetStreamCount()).Returns(0);
        instrumentedMessageProcessor.Setup(iom => iom.GetTypeCount()).Returns(0);
        instrumentedMessageProcessor.Setup(iom => iom.GetAndResetEventsCounter()).Returns(0);

        _diagnosticTypes = new List<DataType>();
        _diagnosticContainers = new List<DataStream>();
        _diagnosticData = new List<string>();

        _fakeDiagnosticsMessageProcessor = new FakeDiagnosticsMessageProcessor(_diagnosticTypes, _diagnosticContainers, _diagnosticData, "machine.service");
    }

    [Fact]
    public void Constructor_InvalidInput_Test()
    {
        var mockLogger = new Mock<IInstrumentedLogger>();
        var mockDiagnosticsMessageProcessor = new Mock<IDiagnosticsMessageProcessor>();
        var instrumentedMessageProcessor = new Mock<IInstrumentedMessageProcessor>();

        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsService(null, mockLogger.Object, UnitTestComponentId, UnitTestComponentType, _elementNode, instrumentedMessageProcessor.Object));
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, null, UnitTestComponentId, UnitTestComponentType, _elementNode, instrumentedMessageProcessor.Object));
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, null, UnitTestComponentType, _elementNode, instrumentedMessageProcessor.Object));
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, UnitTestComponentId, null, _elementNode, instrumentedMessageProcessor.Object));
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, UnitTestComponentId, UnitTestComponentType, null, instrumentedMessageProcessor.Object));
        Assert.Throws<ArgumentNullException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, UnitTestComponentId, UnitTestComponentType, _elementNode, null));
        
        Assert.Throws<ArgumentOutOfRangeException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, string.Empty, string.Empty, _elementNode, instrumentedMessageProcessor.Object));
        Assert.Throws<ArgumentException>(() => new AdapterDiagnosticsService(mockDiagnosticsMessageProcessor.Object, mockLogger.Object, "  ", "  ", _elementNode, instrumentedMessageProcessor.Object));
    }

    [Fact]
    public async Task InitializeAsync_Containers_Types_Sent_Test()
    {
        var instrumentedMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var instrumentedLogger = new Mock<IInstrumentedLogger>();

        using var adapterDiagnosticsService = new AdapterDiagnosticsService(_fakeDiagnosticsMessageProcessor, instrumentedLogger.Object, UnitTestComponentId, UnitTestComponentType, _elementNode, instrumentedMessageProcessor.Object);

        await adapterDiagnosticsService.InitializeAsync();

        Assert.Equal(ExpectedContainerCount, _diagnosticContainers.Count);
        Assert.Equal(ExpectedTypeCount, _diagnosticTypes.Count);
    }

    [Fact]
    public async Task StartAsync_DataMessages_Sent_Test()
    {
        var instrumentedMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var instrumentedLogger = new Mock<IInstrumentedLogger>();
        var expectedDataCount = 3;

        using var adapterDiagnosticsService = new AdapterDiagnosticsService(_fakeDiagnosticsMessageProcessor, instrumentedLogger.Object, UnitTestComponentId, UnitTestComponentType, _elementNode, instrumentedMessageProcessor.Object);

        await adapterDiagnosticsService.StartAsync();

        SpinWait.SpinUntil(() => _diagnosticData.Count.Equals(expectedDataCount), DiagnosticsMessagesRunoutDelayMSecs);
        Assert.Empty(_diagnosticTypes);
        Assert.Empty(_diagnosticContainers);
    }

    [Fact]
    public void ResendTypesAndStreams_StreamCount_Updated_Test()
    {
        var instrumentedMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        var instrumentedLogger = new Mock<IInstrumentedLogger>();
        var expectedDataMessageCount = 4;
        var expectedStreamSuffix = ".StreamCount";

        using var adapterDiagnosticsService = new AdapterDiagnosticsService(_fakeDiagnosticsMessageProcessor, instrumentedLogger.Object, UnitTestComponentId, UnitTestComponentType, _elementNode, instrumentedMessageProcessor.Object);

        adapterDiagnosticsService.ResendTypesAndStreams();

        SpinWait.SpinUntil(() => _diagnosticData.Count.Equals(expectedDataMessageCount), DiagnosticsMessagesRunoutDelayMSecs);

        Assert.Equal(ExpectedTypeCount, _diagnosticTypes.Count);
        Assert.Equal(ExpectedContainerCount, _diagnosticContainers.Count);
        Assert.Equal(expectedDataMessageCount, _diagnosticData.Count);
        Assert.EndsWith(expectedStreamSuffix, _diagnosticData[3]);
    }

    [Fact]
    public async Task StopAsync_CountersCleared_Test()
    {
        var componentId = UnitTestComponentId;
        var componentType = UnitTestComponentType;
        var clearCountersCalled = false;
        var getAndResetCalled = false;

        var instrumentedMessageProcessor = new Mock<IInstrumentedMessageProcessor>();
        instrumentedMessageProcessor.Setup(im => im.ClearCounters()).Callback(() => clearCountersCalled = true);

        var instrumentedLogger = new Mock<IInstrumentedLogger>();
        instrumentedLogger.Setup(il => il.GetAndResetErrorCount()).Returns(0)
            .Callback(() => getAndResetCalled = true);

        using var adapterDiagnosticsService = new AdapterDiagnosticsService(_fakeDiagnosticsMessageProcessor, instrumentedLogger.Object, componentId, componentType, _elementNode, instrumentedMessageProcessor.Object);

        await adapterDiagnosticsService.StopAsync();

        await Task.Delay(DiagnosticsShutdownDelayMsecs);

        Assert.Empty(_diagnosticTypes);
        Assert.Empty(_diagnosticContainers);
        Assert.Empty(_diagnosticData);
        Assert.True(clearCountersCalled);
        Assert.True(getAndResetCalled);
    }
}
