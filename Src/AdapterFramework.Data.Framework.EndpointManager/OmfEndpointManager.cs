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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Health;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.EndpointManager.Tests")]
namespace AdapterFramework.Data.Framework.EndpointManager;

public class OmfEndpointManager : IOmfHealthEndpointManager, IOmfDataEndpointManager, IDisposable
{
    private const string PrintEndpointsMessageTemplate = "{WriterType} will be sent to the following OMF {Placeholder}: {Endpoints}";
    private const string NewEndpointsMessageTemplate = "New OMF {WriterType} {Placeholder} added: {Endpoints}";
    private const string SendErrorMessageTemplate = "Error sending {MessageType} message to endpoint {Writer}.";

    private readonly Dictionary<string, IOmfWriter> _createdWriters = new();
    private readonly ConcurrentDictionary<string, DeviceStatus> _writersDeviceStatus = new();
    private readonly IConfigurationProtector _configurationProtector;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IOmfWriterFactory _writerFactory;
    private readonly ILogManager _logManager;

    private bool _disposed;
    private ILogger _logger;
    private BufferingConfiguration _bufferingConfiguration;
    private ImmutableArray<IOmfWriter> _writers = ImmutableArray.Create<IOmfWriter>();
    private Action<DeviceStatus> _deviceStatusHandler = _ => { };

    /// <summary>
    /// Initializes a new instance of the <see cref="OmfEndpointManager"/> class.
    /// </summary>
    /// <param name="configurationProvider">Configuration provider instance.</param>
    /// <param name="writerFactory">Writer factory.</param>
    /// <param name="logManager">Log manager instance.</param>
    /// <param name="configurationProtector">Configuration protector instance.</param>
    public OmfEndpointManager(
        IConfigurationProvider configurationProvider,
        IOmfWriterFactory writerFactory,
        ILogManager logManager,
        IConfigurationProtector configurationProtector)
    {
        _configurationProvider = configurationProvider;
        _writerFactory = writerFactory;
        _logManager = logManager;
        _configurationProtector = configurationProtector;
    }

    public void Initialize(string componentId, string facet)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(facet, nameof(facet));

        _logger = _logManager.GetOrCreateLogger(componentId);
        _bufferingConfiguration = BufferingConfiguration.GetOrCreateBufferingConfiguration(_configurationProvider, _logger);

        AddOmfWriters(componentId, facet);
    }

    public void SendMessage(ISerializedOmfMessage serializedMessage)
    {
        ThrowHelper.ThrowIfArgumentNull(serializedMessage, nameof(serializedMessage));

        foreach (var writer in _writers)
        {
            try
            {
                writer.SendMessage(serializedMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    SendErrorMessageTemplate,
                    OmfByteHttpClient.GetOmfMessageType(serializedMessage.MessageType).ToString().ToUpperInvariant(),
                    writer);
            }
        }
    }

    public IReadOnlyDictionary<string, long> GetAndResetEgressedValuesCounters()
    {
        var result = new Dictionary<string, long>();

        foreach (var writer in _writers)
        {
            result[writer.Id] = writer.GetAndResetEgressedValuesCounter();
        }

        return result;
    }

    public bool AddRemoveEndpoints(ConfigurationChangedEventArgs configurationChangedEvent, OmfWriterType omfWriterType)
    {
        ThrowHelper.ThrowIfArgumentNull(configurationChangedEvent, nameof(configurationChangedEvent));

        if (configurationChangedEvent.NewValue is EndpointConfigurationBase[] newConfigurations)
        {
            var resendTypesAndStreams = false;
            var oldConfigurations = configurationChangedEvent.OldValue as EndpointConfigurationBase[];
            if (oldConfigurations != null)
            {
                // remove endpoints that don't exist anymore (different Id)
                var oldIdsToRemove = oldConfigurations.Except(newConfigurations, new EndpointConfigurationComparer()).Select(x => x.Id);
                foreach (var oldIdToRemove in oldIdsToRemove)
                {
                    RemoveOmfWriter(oldIdToRemove);
                }
            }

            // use array as foreach iterator is immutable to set it to null
            for (var i = newConfigurations.Length - 1; i >= 0; i--)
            {
                var configuration = newConfigurations[i];
                if (_createdWriters.TryGetValue(configuration.Id, out var writer))
                {
                    var oldConfiguration = oldConfigurations?.FirstOrDefault(x => x.Id == configuration.Id);
                    if (oldConfiguration == null)
                    {
                        continue;
                    }

                    // update old configuration if they are equal (verified with the Except check above).
                    // This relies on the RemoveOmfWriter for-each above for correctness
                    if (RequiresUpdate(configuration, oldConfiguration))
                    {
                        writer.UpdateConfiguration(configuration);

                        // when endpoint URL changes we should send again types and streams
                        if (configuration.Endpoint != oldConfiguration.Endpoint)
                        {
                            resendTypesAndStreams = true;
                        }
                    }

                    newConfigurations[i] = null;
                }
            }

            // add new configurations
            foreach (var configuration in newConfigurations)
            {
                if (configuration == null)
                {
                    continue;
                }

                AddOmfWriter(configuration, omfWriterType, _bufferingConfiguration);
                resendTypesAndStreams = true;
            }

            if (newConfigurations.Length > 0)
            {
                PrintConfiguredEgressEndpoints(newConfigurations, omfWriterType, NewEndpointsMessageTemplate);
            }

            UpdateEgressComponentDeviceStatus();
            return resendTypesAndStreams;
        }

        RemoveOmfWriters();

        UpdateEgressComponentDeviceStatus();
        return false;
    }

    public void SetDeviceStatusHandler(Action<DeviceStatus> deviceStatusHandler)
    {
        _deviceStatusHandler = deviceStatusHandler;
    }

    public Task ResetDataBuffersAsync()
    {
        if (NoOmfWriters())
        {
            _logger.LogWarning("Aborted resetting data buffers - no data buffers to reset.");
            return Task.CompletedTask;
        }

        _logger.LogWarning("Started resetting data buffers.");
        RemoveOmfWriters();
        AddOmfWriters(OmfEgressComponentId, DataEndpointsFacetName);
        _logger.LogWarning("Completed resetting data buffers.");
        return Task.CompletedTask;
    }

    public void UpdateBufferSize(int newMaxBufferSizeMB)
    {
        if (_bufferingConfiguration == null)
            return;

        _bufferingConfiguration.MaxBufferSizeMB = newMaxBufferSizeMB;
        foreach (var writer in _writers)
        {
            if (writer is OmfWriter omfWriter)
            {
                omfWriter.UpdateBufferSize(newMaxBufferSizeMB);
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal bool RequiresUpdate(EndpointConfigurationBase config1, EndpointConfigurationBase config2)
    {
        return !_configurationProtector.ProtectedConfigsEqual(ref config1, ref config2);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        foreach (var writer in _writers)
        {
            writer?.Dispose();
        }

        _disposed = true;
    }

    private void AddOmfWriters(string componentId, string facet)
    {
        var writerType = facet.Equals(DataEndpointsFacetName, StringComparison.OrdinalIgnoreCase) ? OmfWriterType.Data : OmfWriterType.Health;

        if (_configurationProvider.TryGetConfiguration<EndpointConfigurationBase[]>(componentId, facet, out var configurations, out var getErrors))
        {
            foreach (var config in configurations)
            {
                AddOmfWriter(config, writerType, _bufferingConfiguration);
            }

            PrintConfiguredEgressEndpoints(configurations, writerType, PrintEndpointsMessageTemplate);

            _logger.LogInformation("Persistent buffering enabled: {BufferingEnabled}.", _bufferingConfiguration.EnablePersistentBuffering);

            UpdateEgressComponentDeviceStatus();
        }
        else if (!getErrors.IsNullOrEmpty())
        {
            _logger.LogError("Error creating {Facet} endpoints. Validation errors: {Errors}", facet, getErrors);
        }
        else
        {
            _logger.LogInformation("No {Facet} configured.", facet);
        }
    }

    private bool NoOmfWriters() => _createdWriters.IsEmpty();

    private void RemoveOmfWriters()
    {
        var writersToRemove = new List<string>(_createdWriters.Keys);
        foreach (var writerToRemove in writersToRemove)
        {
            RemoveOmfWriter(writerToRemove);
        }
    }

    private void RemoveOmfWriter(string id)
    {
        if (_createdWriters.TryGetValue(id, out var writer))
        {
            _writers = _writers.Remove(writer);
            _createdWriters.Remove(id);
            writer.Dispose();
            writer.DeleteBuffers();
            _writersDeviceStatus.TryRemove(id, out _);
            UpdateEgressComponentDeviceStatus();
            _logger.LogInformation("Endpoint {Id} has been removed.", id);
        }
    }

    private void PrintConfiguredEgressEndpoints(IEnumerable<EndpointConfigurationBase> configuredEgressEndpoints, OmfWriterType writerType, string messageTemplate)
    {
        var endpointsStringBuilder = new StringBuilder();
        var endpointsCount = 0;

        foreach (var configuredEgressEndpoint in configuredEgressEndpoints)
        {
            if (configuredEgressEndpoint != null)
            {
                endpointsCount++;
                endpointsStringBuilder.AppendLine(configuredEgressEndpoint.ToString());
            }
        }

        if (endpointsCount == 0)
        {
            return;
        }

        var writerTypeString = writerType switch
        {
            OmfWriterType.Health => writerType + " data",
            _ => writerType.ToString(),
        };

        _logger.LogInformation(messageTemplate, writerTypeString, endpointsCount > 1 ? "endpoints" : "endpoint", endpointsStringBuilder);
    }

    private void AddOmfWriter(IEndpointConfiguration writerConfig, OmfWriterType writerType, IBufferingConfiguration bufferingConfig)
    {
        try
        {
            var writer = _writerFactory.GetOmfWriterInstance(writerConfig, _logger, writerType, bufferingConfig, ErrorCallback);
            _writersDeviceStatus.TryAdd(writerConfig.Id, DeviceStatus.Starting);
            _createdWriters.Add(writerConfig.Id, writer);
            _writers = _writers.Add(writer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OMF writer for endpoint {Endpoint}. Data for this endpoint is not going to be buffered or sent. Please contact endpoint provider Tech Support for assistance.", writerConfig.Endpoint);
        }

        void ErrorCallback(bool x) => UpdateWriterDeviceStatus(writerConfig.Id, x);
    }

    private void UpdateWriterDeviceStatus(string id, bool inError)
    {
        if (_writersDeviceStatus.TryGetValue(id, out var previousDeviceStatus))
        {
            var currentStatus = inError ? DeviceStatus.DeviceInError : DeviceStatus.Good;
            if (previousDeviceStatus != currentStatus)
            {
                _writersDeviceStatus.TryUpdate(id, currentStatus, previousDeviceStatus);
                UpdateEgressComponentDeviceStatus();
            }
        }
    }

    private void UpdateEgressComponentDeviceStatus()
    {
        _deviceStatusHandler(_writersDeviceStatus.Values.Any(status => status == DeviceStatus.DeviceInError)
            ? DeviceStatus.DeviceInError
            : DeviceStatus.Good);
    }
}
