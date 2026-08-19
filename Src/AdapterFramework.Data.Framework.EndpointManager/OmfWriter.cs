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
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Buffering;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Buffering;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Polly;
using Polly.Contrib.WaitAndRetry;
using static AdapterFramework.Data.Framework.Buffering.BufferingConstants;

namespace AdapterFramework.Data.Framework.EndpointManager;

/// <summary>
/// Provides methods to send OMF messages.
/// </summary>
public class OmfWriter : IOmfWriter
{
    #region Constants

    private const int MaxMetadataFilesCount = 3;
    private const int MaxBackoffRetryCount = 50;
    private const string RequestNotRetriedMessage = "This request will not be retried. For more information, increase LogLevel to Debug and regenerate the request.";
    private const string PartiallyProcessedMessage = "The endpoint may have processed parts of the message successfully.";
    private const string BadRequestLogMessage = "Endpoint {0} returned \"Bad Request\" response, most likely indicating a malformed OMF message. No parts of this message were processed. " + RequestNotRetriedMessage;
    private const string ConflictRequestLogMessage = "Endpoint {0} returned \"Conflict\" response, indicating that an attempt to change at least one immutable property on OMF information was made. " + PartiallyProcessedMessage + " " + RequestNotRetriedMessage;
    private const string NotImplementedLogMessage = "Endpoint {0} returned \"Not Implemented\" response, indicating that message used one or more OMF features that are not supported by the endpoint. " + PartiallyProcessedMessage + " " + RequestNotRetriedMessage;
    private const string InternalServerErrorLogMessage = "Endpoint {0} returned \"Internal Server Error\" response, indicating that message processing failed on the server. " + PartiallyProcessedMessage + " " + RequestNotRetriedMessage;
    private const string ForbiddenErrorLogMessage = "Endpoint {0} returned \"Forbidden\" response, indicating that message processing failed on the server. " + PartiallyProcessedMessage + " " + RequestNotRetriedMessage;
    private const string NotFoundErrorLogMessage = "Endpoint {0} returned \"Not Found\" response, indicating that message processing failed on the server. " + PartiallyProcessedMessage + " " + RequestNotRetriedMessage;
    private const string GenericErrorRequestMessage = "Endpoint {0} returned an error response. " + RequestNotRetriedMessage;
    private const string SentMessageDebugMessage = "The request that was sent to the endpoint: {SentMessageBody}";
    private const string EndpointErrorClearedMessage = "Error has been resolved and data will be successfully processed by {Uri}.";
    private const string EndpointInErrorMessage = "No data from this adapter will be successfully processed by endpoint {Uri} until error is resolved.";

    #endregion

    #region Private Fields

    private readonly ILogger _logger;
    private readonly ISerializer _serializer;
    private readonly ICompressor _compressor;
    private readonly IClient _client;
    private readonly BackedUpOmfMessageQueue _typesAndStreamsQueue;
    private readonly BackedUpOmfMessageQueue _dataQueue;
    private readonly FileQueue _typesAndStreamsFileQueue;
    private readonly FileQueue _dataFileQueue;
    private readonly IPersistentMessageQueue<ISerializedOmfMessage> _persistentTypesAndStreamsQueue;
    private readonly IPersistentMessageQueue<ISerializedOmfMessage> _persistentDataQueue;
    private readonly Task _bufferConsumerTask;
    private readonly CancellationTokenSource _writerCts;
    private readonly TimeSpan _maxRetryDelay = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _retryFirstDelay = TimeSpan.FromSeconds(1);
    private readonly OmfWriterType _writerType;

    private Action<bool> _statusUpdateCallback = obj => { };
    private IEndpointConfiguration _configuration;
    private bool _endpointInError;
    private bool _firstStatusSent;
    private bool _disposed;
    private long _valueCount;
    private MessageType _messageType;
    private bool _shouldRetry404 = true;
    private EndpointResponse _cachedResponse;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new instance of the <see cref="OmfWriter"/> class.
    /// </summary>
    /// <param name="writerConfiguration"><see cref="IEndpointConfiguration"/> configuration instance.</param>
    /// <param name="logger">System <see cref="ILogger"/> logger instance.</param>
    /// <param name="serializer"><see cref="ISerializer"/> instance.</param>
    /// <param name="compressor"><see cref="ICompressor"/> instance.</param>
    /// <param name="dataProtector"><see cref="IEdgeDataProtector"/> instance.</param>
    /// <param name="bufferingConfiguration"><see cref="IBufferingConfiguration"/> instance.</param>
    /// <param name="applicationManifest"><see cref="IApplicationManifest"/> instance.</param>
    /// <param name="bufferFilesPath">Full path to directory where buffer files should be stored.</param>
    /// <param name="debugLogsPath">Full path to directory where egress debug logs should be stored.</param>
    /// <param name="writerType">Type of the OMF writer instance (Data or Health).</param>
    /// <param name="inErrorStatusCallback">Will send true if endpoint in error, false if OK.</param>
    /// <param name="edgeEventProvider">Optional <see cref="IEdgeEventProvider"/> instance.</param>
    public OmfWriter(
        IEndpointConfiguration writerConfiguration,
        ILogger logger,
        ISerializer serializer,
        ICompressor compressor,
        IEdgeDataProtector dataProtector,
        IBufferingConfiguration bufferingConfiguration,
        IApplicationManifest applicationManifest,
        string bufferFilesPath,
        string debugLogsPath,
        OmfWriterType writerType,
        Action<bool> inErrorStatusCallback,
        IEdgeEventProvider edgeEventProvider = null)
    {
        ThrowHelper.ThrowIfArgumentNull(writerConfiguration, nameof(writerConfiguration));
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNull(serializer, nameof(serializer));
        ThrowHelper.ThrowIfArgumentNull(dataProtector, nameof(dataProtector));
        ThrowHelper.ThrowIfArgumentNull(bufferingConfiguration, nameof(bufferingConfiguration));
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(bufferFilesPath, nameof(bufferFilesPath));

        Id = writerConfiguration.Id;
        _configuration = writerConfiguration;
        _logger = logger;
        _serializer = serializer;
        _compressor = compressor;

        if (inErrorStatusCallback != null)
        {
            _statusUpdateCallback = inErrorStatusCallback;
        }

        _client = new OmfByteHttpClient(
            writerConfiguration,
            dataProtector,
            applicationManifest,
            logger,
            compressor,
            serializer.Format,
            debugLogsPath,
            edgeEventProvider);

        _writerType = writerType;
        _writerCts = new CancellationTokenSource();

        if (bufferingConfiguration.EnablePersistentBuffering)
        {
            var targetIdentifier = _client.Uri.ToString();

            var maxBufferFiles = bufferingConfiguration.MaxBufferSizeMB / MaxBufferFileSizeMb;
            if (maxBufferFiles < 1)
            {
                maxBufferFiles = 1;
            }

            _persistentTypesAndStreamsQueue = new PersistentOmfMessageQueue(
                targetIdentifier,
                _typesAndStreamsFileQueue = new FileQueue(bufferFilesPath, MetadataBufferFilePrefix, _configuration.Id, MaxBufferFileSizeMb, MaxMetadataFilesCount, logger),
                logger);

            _persistentDataQueue = new PersistentOmfMessageQueue(
                targetIdentifier,
                _dataFileQueue = new FileQueue(bufferFilesPath, DataBufferFilePrefix, _configuration.Id, MaxBufferFileSizeMb, maxBufferFiles, logger, edgeEventProvider),
                logger);
        }

        _typesAndStreamsQueue = new BackedUpOmfMessageQueue(DefaultVolatileMemorySizeMb, DefaultMessageExpirationTime, _persistentTypesAndStreamsQueue, logger);
        _dataQueue = new BackedUpOmfMessageQueue(GetDataQueueVolatileMemorySize(bufferingConfiguration, writerType), DefaultMessageExpirationTime, _persistentDataQueue, logger);
        _bufferConsumerTask = Task.Run(BufferedOmfMessageHandlerAsync);
    }

    #endregion

    #region Properties

    public string Id { get; }

    private bool EndpointInError
    {
        get
        {
            return _endpointInError;
        }

        set
        {
            _statusUpdateCallback(value);
            _endpointInError = value;
        }
    }

    #endregion

    #region IOmfWriter

    public long GetAndResetEgressedValuesCounter()
    {
        return Interlocked.Exchange(ref _valueCount, 0);
    }

    public void UpdateConfiguration(IEndpointConfiguration configuration)
    {
        if (configuration == null)
        {
            return;
        }

        if (!_configuration.Id.Equals(configuration.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Updated writer configuration must have the same ID.");
        }

        _configuration = configuration;
        (_client as OmfByteHttpClient)?.UpdateConfiguration(configuration);

        _logger.LogInformation(@"OMF {WriterType} Endpoint configuration has been updated: {Endpoint}", _writerType, configuration.ToString());
    }

    /// <inheritdoc/>
    public void SendMessage(ISerializedOmfMessage serializedMessage)
    {
        ThrowHelper.ThrowIfArgumentNull(serializedMessage, nameof(serializedMessage));

        if (IsTypeOrContainer(serializedMessage.MessageType))
        {
            _typesAndStreamsQueue.Enqueue(serializedMessage);
        }
        else
        {
            _dataQueue.Enqueue(serializedMessage);
        }
    }

    public void UpdateBufferSize(int newMaxBufferSizeMB)
    {
        int maxQueueFiles = newMaxBufferSizeMB / MaxBufferFileSizeMb;
        if (maxQueueFiles < 1)
        {
            maxQueueFiles = 1;
        }

        _dataFileQueue?.UpdateMaxQueueFiles(maxQueueFiles);
    }

    /// <inheritdoc/>
    public void DeleteBuffers()
    {
        _dataQueue.Clear();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _statusUpdateCallback = obj => { };
            _writerCts.Cancel();

            if (_bufferConsumerTask != null)
            {
                try
                {
                    _bufferConsumerTask?.GetAwaiter().GetResult();
                }
                catch (AggregateException ae)
                {
                    ae.Handle(ex => ex is TaskCanceledException);
                }
                finally
                {
                    _bufferConsumerTask.Dispose();
                }
            }

            _typesAndStreamsQueue.Dispose();
            _dataQueue.Dispose();
            _client.Dispose();
            _writerCts.Dispose();

            _typesAndStreamsFileQueue?.Dispose();
            _dataFileQueue?.Dispose();
            _persistentDataQueue?.Dispose();
            _persistentTypesAndStreamsQueue?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private static bool IsTypeOrContainer(MessageType messageType) => messageType == MessageType.Type || messageType == MessageType.Container;

    private static int GetDataQueueVolatileMemorySize(IBufferingConfiguration bufferingConfiguration, OmfWriterType writerType)
    {
        if (!bufferingConfiguration.EnablePersistentBuffering)
        {
            var volatileMemorySizeMb = writerType == OmfWriterType.Health
                ? DefaultVolatileMemorySizeMb
                : bufferingConfiguration.MaxBufferSizeMB;

            return volatileMemorySizeMb;
        }

        return DefaultVolatileMemorySizeMb;
    }

    private bool IsRetryable404MessageType(MessageType messageType) =>
        _shouldRetry404 && (messageType == MessageType.Data || messageType == MessageType.StaticData || messageType == MessageType.DynamicData);

    private bool RequiresRequeue(EndpointResponse response)
    {
        switch (response.ResponseStatus)
        {
            case ResponseStatusEnum.NotFound:
                if (IsRetryable404MessageType(_messageType))
                {
                    _cachedResponse = response;
                    _shouldRetry404 = false;
                    return true;
                }

                _shouldRetry404 = true;
                return false;
            case ResponseStatusEnum.DelayRequired:
            case ResponseStatusEnum.Fail:
                _shouldRetry404 = true;
                return true;
            default:
                _shouldRetry404 = true;
                return false;
        }
    }

    private async Task BufferedOmfMessageHandlerAsync()
    {
        try
        {
            while (!_writerCts.Token.IsCancellationRequested)
            {
                if (_typesAndStreamsQueue.TryPeek(out var message))
                {
                    await ProcessMessageAsync(message, _typesAndStreamsQueue);
                }
                else if (_dataQueue.TryPeek(out message))
                {
                    await ProcessMessageAsync(message, _dataQueue);
                }
                else
                {
                    await Task.Delay(BufferRetryInterval);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OMF message buffer consumer task stopped. Endpoint: {Endpoint}.", _client.Uri);
        }
    }

    private async Task ProcessMessageAsync(ISerializedOmfMessage message, BackedUpOmfMessageQueue queue)
    {
        if (await SendBufferedMessageAsync(message))
        {
            queue.TryDequeue(out _);
            Interlocked.Add(ref _valueCount, message.ItemCount);
        }
    }

    private async Task<bool> SendBufferedMessageAsync(ISerializedOmfMessage serializedMessage)
    {
        if (ShouldNotRetryFailed404Message(serializedMessage))
        {
            HandleResponse(_cachedResponse, serializedMessage);
            _messageType = serializedMessage.MessageType;
            return true;
        }

        _messageType = serializedMessage.MessageType;

        var body = serializedMessage.MessageBody;
        var messageAction = serializedMessage.MessageAction;
        var omfVersion = serializedMessage.OmfVersion;
        var retryDelays = Backoff.DecorrelatedJitterBackoffV2(_retryFirstDelay, fastFirst: false, retryCount: MaxBackoffRetryCount)
            .Select(s => TimeSpan.FromTicks(Math.Min(s.Ticks, _maxRetryDelay.Ticks)));

        var defaultRetryPolicy = Policy.HandleResult<EndpointResponse>(r => r.ResponseStatus == ResponseStatusEnum.Fail).WaitAndRetryAsync(retryDelays);
        var endpointRequestedRetryPolicy = Policy.HandleResult<EndpointResponse>(r => r.ResponseStatus == ResponseStatusEnum.DelayRequired).WaitAndRetryAsync(MaxBackoffRetryCount,
            (x, endpointResponseDelegate, z) => endpointResponseDelegate.Result.Delay, async (x, y, z, a) => { await Task.CompletedTask; });
        var policy = defaultRetryPolicy.WrapAsync(endpointRequestedRetryPolicy);

        if (!_writerCts.IsCancellationRequested)
        {
            try
            {
                if (!RequiresRequeue(await policy.ExecuteAsync(ct => SendMessageAndProcessResponseAsync(_messageType, body, serializedMessage, messageAction, omfVersion, ct),
                    _writerCts.Token)))
                {
                    return true;
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private bool ShouldNotRetryFailed404Message(ISerializedOmfMessage serializedMessage)
    {
        //// This checks whether we have sent Type or Container to the endpoint after 404 received for a data message.
        //// When no type/container/schema message was sent before this there is no reason to retry the send operation.
        return !_shouldRetry404
               && _messageType != MessageType.Type
               && _messageType != MessageType.Container
               && _messageType != MessageType.Schema
               && serializedMessage.MessageType != MessageType.Type
               && serializedMessage.MessageType != MessageType.Container
               && serializedMessage.MessageType != MessageType.Schema;
    }

    private async Task<EndpointResponse> SendMessageAndProcessResponseAsync(
        MessageType messageType,
        byte[] messageBody,
        ISerializedOmfMessage serializedMessage,
        MessageAction messageAction,
        OmfVersion omfVersion,
        CancellationToken cancellationToken)
    {
        var response = await _client.SendMessageAsync(messageType, messageBody, messageAction, omfVersion, cancellationToken, serializedMessage.PartitionKey);
        HandleResponse(response, serializedMessage, true);
        return response;
    }

    /// <summary>
    /// Checks the response given by the endpoint and returns whether the message sent needs to be requeued.
    /// </summary>
    /// <param name="response">Response given by the endpoint.</param>
    /// <param name="serializedMessage">Message sent to the endpoint.</param>
    /// <param name="buffered">Whether the message was buffered.</param>
    private void HandleResponse(EndpointResponse response, ISerializedOmfMessage serializedMessage, bool buffered = false)
    {
        switch (response.ResponseStatus)
        {
            case ResponseStatusEnum.Conflict:
            case ResponseStatusEnum.BadRequest:
            case ResponseStatusEnum.InternalServerError:
            case ResponseStatusEnum.NotImplemented:
            case ResponseStatusEnum.Forbidden:
                PrintNoRepeatErrorMessage(response.ResponseStatus, serializedMessage, response.Message);
                break;
            case ResponseStatusEnum.NotFound:
                if (_messageType == MessageType.Container || _messageType == MessageType.Type)
                {
                    PrintNoRepeatErrorMessage(response.ResponseStatus, serializedMessage, response.Message);
                    break;
                }

                if (!_shouldRetry404)
                {
                    PrintNoRepeatErrorMessage(response.ResponseStatus, serializedMessage, response.Message);
                    _shouldRetry404 = true;
                }

                break;
            case ResponseStatusEnum.Fail:
                PrintConnectionErrorMessage(serializedMessage.MessageType, response.Message, buffered);
                break;
            case ResponseStatusEnum.DelayRequired:
                PrintConnectionErrorMessage(serializedMessage.MessageType, response.Message, buffered);
                break;
            case ResponseStatusEnum.Success:
                PrintSuccessResponseMessage(response.Message);
                break;
            default: // impossible
                return;
        }
    }

    private void PrintSuccessResponseMessage(string responseMessage)
    {
        if (!_firstStatusSent)
        {
            EndpointInError = false;
            _firstStatusSent = true;
        }

        if (EndpointInError)
        {
            EndpointInError = false;
            _logger.LogInformation(EndpointErrorClearedMessage, _client.Uri);
        }

        _logger.LogTrace("{ResponseMessage}", responseMessage);
    }

    /// <summary>
    /// Logs messages for non-retryable endpoint responses.
    /// </summary>
    /// <param name="responseStatus">The status enum for the response.</param>
    /// <param name="sentMessage">The message sent to the endpoint.</param>
    /// <param name="receivedMessage">The response message content.</param>
    private void PrintNoRepeatErrorMessage(ResponseStatusEnum responseStatus, ISerializedOmfMessage sentMessage, string receivedMessage)
    {
        var logMessage = responseStatus switch
        {
            ResponseStatusEnum.BadRequest => string.Format(CultureInfo.InvariantCulture, BadRequestLogMessage, _client.Uri),
            ResponseStatusEnum.Conflict => string.Format(CultureInfo.InvariantCulture, ConflictRequestLogMessage, _client.Uri),
            ResponseStatusEnum.InternalServerError => string.Format(CultureInfo.InvariantCulture, InternalServerErrorLogMessage, _client.Uri),
            ResponseStatusEnum.NotImplemented => string.Format(CultureInfo.InvariantCulture, NotImplementedLogMessage, _client.Uri),
            ResponseStatusEnum.Forbidden => string.Format(CultureInfo.InvariantCulture, ForbiddenErrorLogMessage, _client.Uri),
            ResponseStatusEnum.NotFound => string.Format(CultureInfo.InvariantCulture, NotFoundErrorLogMessage, _client.Uri),
            _ => string.Format(CultureInfo.InvariantCulture, GenericErrorRequestMessage, _client.Uri),
        };

        _logger.LogError("{NotRetryableError}", logMessage);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            // If we expect the endpoint to send a useful message, we can log the message too.
            object sentOmfMessageBody = string.Empty;
            try
            {
                sentOmfMessageBody = GetOmfMessageText(sentMessage.MessageBody);
            }
            catch (Exception)
            {
                // This is really bad if this happens. This is here because we create bad messages in unit tests.
                _logger.LogError("Could not deserialize sent message.");
            }

            _logger.LogDebug(SentMessageDebugMessage, sentOmfMessageBody);
            _logger.LogDebug("The response that was received from the endpoint: {ReceivedMessage}", receivedMessage);
        }
    }

    private void PrintConnectionErrorMessage(MessageType type, string message, bool buffered)
    {
        if (!buffered)
        {
            _logger.LogError("Error sending {MessageType} message to the endpoint {Endpoint}: {Message}", OmfByteHttpClient.GetOmfMessageType(type).ToString().ToUpperInvariant(), _client.Uri, message);
        }
        else if (!EndpointInError)
        {
            // Only send once (!EndpointInError). Don't want to spam users with the same error if the message will be sent eventually
            _logger.LogError("Error sending {MessageType} message to endpoint {Endpoint}. Sending this data will be retried. Error: {Message}", OmfByteHttpClient.GetOmfMessageType(type).ToString().ToUpperInvariant(), _client.Uri, message);
        }

        if (!EndpointInError || !_firstStatusSent)
        {
            _firstStatusSent = true;
            _logger.LogError(EndpointInErrorMessage, _client.Uri);
            EndpointInError = true;
        }
    }

    private object GetOmfMessageText(byte[] omfMessageSent)
    {
        return _serializer.Deserialize(_compressor?.Decompress(omfMessageSent) ?? omfMessageSent);
    }

    #endregion

}
