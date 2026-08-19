// Copyright 2026 AVEVA Group Limited
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

using AdapterFramework.Data.Adapter.WeatherGovAdapter.Configuration;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataCollection;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.DataProcessor;
using AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.TestData;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AdapterFramework.Data.Adapter.WeatherGovAdapter.UnitTests.DataProcessor;

/// <summary>
/// Unit tests for <see cref="MetadataService"/>: type/stream registration and value egress
/// through <see cref="IAdapterMessageProcessor"/>. Moq stands in for the processor so the
/// write contracts and their call counts can be verified.
/// </summary>
public class MetadataServiceTests
{
    private const string TypeId = "test-type-id";
    private readonly WeatherMeasurementMapper _measurementMapper = new();

    private static MetadataService CreateService(
        IAdapterMessageProcessor processor,
        WeatherMeasurementMapper measurementMapper,
        string typeId = TypeId) =>
        new(() => processor, () => typeId, measurementMapper);

    // Builds a strict processor mock with all three write methods set up so the full write path
    // (type + stream + value) can run. Strict behavior means any call that is NOT set up fails the
    // test, so verifying specific writes also asserts that no other write happens.
    private static Mock<IAdapterMessageProcessor> CreateStrictProcessorWithWriteSetups()
    {
        var processor = new Mock<IAdapterMessageProcessor>(MockBehavior.Strict);
        processor.Setup(p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()));
        processor.Setup(p => p.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()));
        processor.Setup(p => p.WriteDynamicValue(
            It.IsAny<IDataSelectionConfiguration>(),
            It.IsAny<WeatherObservationMeasurement>(),
            It.IsAny<MessageAction>(),
            It.IsAny<PartitionKey?>()));
        return processor;
    }

    // EnsureTypeRegistered

    /// <summary>
    /// Verifies that EnsureTypeRegistered does nothing when the processor is null.
    /// </summary>
    [Fact]
    public void EnsureTypeRegistered_NullProcessor_DoesNothing()
    {
        var service = CreateService(processor: null, measurementMapper: _measurementMapper);

        var exception = Record.Exception(() => service.EnsureTypeRegistered());

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that the first call to EnsureTypeRegistered writes the type exactly once.
    /// </summary>
    [Fact]
    public void EnsureTypeRegistered_FirstCall_WritesTypeOnce()
    {
        var processor = new Mock<IAdapterMessageProcessor>(MockBehavior.Strict);
        DataType written = null;
        processor
            .Setup(p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()))
            .Callback<DataType, MessageAction>((dataType, _) => written = dataType);
        var service = CreateService(processor.Object, _measurementMapper);

        service.EnsureTypeRegistered();

        processor.Verify(
            p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()),
            Times.Once);
        Assert.NotNull(written);
        Assert.Equal(
            new[]
            {
                "Timestamp",
                "barometric_pressure_pa",
                "dewpoint_c",
                "relative_humidity_pct",
                "temperature_c",
                "text_description",
                "visibility_m",
                "wind_direction_deg",
                "wind_speed_mps",
            },
            written.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Verifies that repeated calls to EnsureTypeRegistered write the type only once.
    /// </summary>
    [Fact]
    public void EnsureTypeRegistered_IsIdempotent()
    {
        var processor = new Mock<IAdapterMessageProcessor>(MockBehavior.Strict);
        processor.Setup(p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()));
        var service = CreateService(processor.Object, _measurementMapper);

        service.EnsureTypeRegistered();
        service.EnsureTypeRegistered();

        processor.Verify(
            p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that EnsureTypeRegistered logs the registered type when a logger is provided.
    /// </summary>
    [Fact]
    public void EnsureTypeRegistered_WithLogger_LogsTypeRegistration()
    {
        var processor = new Mock<IAdapterMessageProcessor>(MockBehavior.Strict);
        processor.Setup(p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()));
        var service = CreateService(processor.Object, _measurementMapper);
        var logger = MockLoggerHelpers.CreateLogger();

        service.EnsureTypeRegistered(logger.Object);

        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, "Sending type WeatherObservationMeasurement", Times.Once());
        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, TypeId, Times.Once());
    }

    // Write

    /// <summary>
    /// Verifies that Write does nothing when the processor is null.
    /// </summary>
    [Fact]
    public void Write_NullProcessor_DoesNothing()
    {
        var service = CreateService(processor: null, measurementMapper: _measurementMapper);

        var exception = Record.Exception(() => service.Write(
            WeatherGovTestData.CreateValidSelectionItem(),
            WeatherGovTestData.CreateMappedMeasurementFromFullObservation()));

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that a blank stream id still registers the type but writes no stream or value.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Write_BlankStreamId_WritesNothing(string streamId)
    {
        var processor = new Mock<IAdapterMessageProcessor>(MockBehavior.Strict);
        processor.Setup(p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()));
        var service = CreateService(processor.Object, _measurementMapper);

        service.Write(
            WeatherGovTestData.CreateValidSelectionItem(streamId),
            WeatherGovTestData.CreateMappedMeasurementFromFullObservation());

        // Write() registers the type before it inspects the stream id, so the type is still written
        // even though the blank stream id stops the stream and value writes.
        processor.Verify(
            p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()),
            Times.Once);
        processor.Verify(
            p => p.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()),
            Times.Never);
        processor.Verify(
            p => p.WriteDynamicValue(
                It.IsAny<IDataSelectionConfiguration>(),
                It.IsAny<WeatherObservationMeasurement>(),
                It.IsAny<MessageAction>(),
                It.IsAny<PartitionKey?>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that the first write for a stream writes the type, then the stream, then the value.
    /// </summary>
    [Fact]
    public void Write_FirstTimeForStream_WritesStreamThenValue()
    {
        var processor = new Mock<IAdapterMessageProcessor>(MockBehavior.Strict);

        // Strict mock plus MockSequence only matches these writes in the declared order, so a
        // regression that reordered them (such as value before stream) throws inside Write.
        var writeSequence = new MockSequence();
        processor.InSequence(writeSequence)
            .Setup(p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()));
        processor.InSequence(writeSequence)
            .Setup(p => p.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()));
        processor.InSequence(writeSequence)
            .Setup(p => p.WriteDynamicValue(
                It.IsAny<IDataSelectionConfiguration>(),
                It.IsAny<WeatherObservationMeasurement>(),
                It.IsAny<MessageAction>(),
                It.IsAny<PartitionKey?>()));
        var service = CreateService(processor.Object, _measurementMapper);
        var item = WeatherGovTestData.CreateValidSelectionItem("WEATHERGOV.STATION.KSEA");
        var measurement = WeatherGovTestData.CreateMappedMeasurementFromFullObservation();

        service.Write(item, measurement);

        processor.Verify(
            p => p.WriteStream(
                It.Is<DataStream>(stream =>
                    stream.Id == item.StreamId &&
                    stream.Name == item.Name &&
                    stream.TypeId == TypeId),
                It.IsAny<MessageAction>()),
            Times.Once);
        processor.Verify(
            p => p.WriteDynamicValue(
                It.Is<IDataSelectionConfiguration>(config => config == item),
                It.Is<WeatherObservationMeasurement>(value => value == measurement),
                It.IsAny<MessageAction>(),
                It.IsAny<PartitionKey?>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the first write logs the type, stream, and measurement payload at information level.
    /// </summary>
    [Fact]
    public void Write_FirstTimeForStream_LogsTypeStreamAndMeasurement()
    {
        var processor = CreateStrictProcessorWithWriteSetups();
        var service = CreateService(processor.Object, _measurementMapper);
        var item = WeatherGovTestData.CreateValidSelectionItem("WEATHERGOV.STATION.KSEA");
        var measurement = WeatherGovTestData.CreateMappedMeasurementFromFullObservation();
        var logger = MockLoggerHelpers.CreateLogger();

        service.Write(item, measurement, logger.Object);

        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, "Sending type WeatherObservationMeasurement", Times.Once());
        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, "Sending stream KSEA Station with StreamId WEATHERGOV.STATION.KSEA", Times.Once());
        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, "Sending measurement for stream WEATHERGOV.STATION.KSEA", Times.Once());
        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, "temperature_c", Times.Once());
        MockLoggerHelpers.VerifyLog(logger, LogLevel.Information, "timestamp", Times.Once());
    }

    /// <summary>
    /// Verifies that writing the same stream id twice writes the stream once but the value each time.
    /// </summary>
    [Fact]
    public void Write_SameStreamIdTwice_WritesStreamOnceButValueEachTime()
    {
        var processor = CreateStrictProcessorWithWriteSetups();
        var service = CreateService(processor.Object, _measurementMapper);
        var item = WeatherGovTestData.CreateValidSelectionItem("WEATHERGOV.STATION.KSEA");

        service.Write(item, WeatherGovTestData.CreateMappedMeasurementFromFullObservation());
        service.Write(item, WeatherGovTestData.CreateMappedMeasurementFromFullObservation());

        processor.Verify(
            p => p.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()),
            Times.Once);
        processor.Verify(
            p => p.WriteDynamicValue(
                It.IsAny<IDataSelectionConfiguration>(),
                It.IsAny<WeatherObservationMeasurement>(),
                It.IsAny<MessageAction>(),
                It.IsAny<PartitionKey?>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that writing different stream ids writes a stream for each.
    /// </summary>
    [Fact]
    public void Write_DifferentStreamIds_WritesStreamForEach()
    {
        var processor = CreateStrictProcessorWithWriteSetups();
        var service = CreateService(processor.Object, _measurementMapper);

        service.Write(
            WeatherGovTestData.CreateValidSelectionItem("WEATHERGOV.STATION.KSEA"),
            WeatherGovTestData.CreateMappedMeasurementFromFullObservation());
        service.Write(
            WeatherGovTestData.CreateValidSelectionItem("WEATHERGOV.STATION.KPDX"),
            WeatherGovTestData.CreateMappedMeasurementFromFullObservation());

        processor.Verify(
            p => p.WriteStream(
                It.Is<DataStream>(stream => stream.Id == "WEATHERGOV.STATION.KSEA"),
                It.IsAny<MessageAction>()),
            Times.Once);
        processor.Verify(
            p => p.WriteStream(
                It.Is<DataStream>(stream => stream.Id == "WEATHERGOV.STATION.KPDX"),
                It.IsAny<MessageAction>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that concurrent writes for the same stream register the type and stream once while
    /// writing a value for every call.
    /// </summary>
    [Fact]
    public void Write_ConcurrentSameStreamId_WritesStreamOnce()
    {
        var processor = CreateStrictProcessorWithWriteSetups();
        var service = CreateService(processor.Object, _measurementMapper);
        var item = WeatherGovTestData.CreateValidSelectionItem("WEATHERGOV.STATION.KSEA");
        const int writerCount = 64;

        Parallel.For(0, writerCount, _ =>
            service.Write(item, WeatherGovTestData.CreateMappedMeasurementFromFullObservation()));

        processor.Verify(
            p => p.WriteType(It.IsAny<DataType>(), It.IsAny<MessageAction>()),
            Times.Once);
        processor.Verify(
            p => p.WriteStream(It.IsAny<DataStream>(), It.IsAny<MessageAction>()),
            Times.Once);
        processor.Verify(
            p => p.WriteDynamicValue(
                It.IsAny<IDataSelectionConfiguration>(),
                It.IsAny<WeatherObservationMeasurement>(),
                It.IsAny<MessageAction>(),
                It.IsAny<PartitionKey?>()),
            Times.Exactly(writerCount));
    }
}
