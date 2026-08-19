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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.DataModel.Extensions;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Events;
using AdapterFramework.Data.Framework.Abstractions.HttpCommunication;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.HttpCommunication;
using AdapterFramework.Data.Framework.Extensions;

using static AdapterFramework.Data.Framework.Common.HttpCommunication.EndpointManagerConstants;

namespace AdapterFramework.Data.Framework.EndpointManager;

/// <summary>
/// IClient implementation that uses HTTP client, accepts byte[] representation of serialized OMF messages and
/// sends them to configured OMF Endpoint over HTTP or HTTPS.
/// </summary>
public class OmfByteHttpClient : HttpClientWrapper, IClient
{
    private const string RequestString = "Request";
    private const string ResponseString = "Response";

    private readonly ICompressor _compressor;
    private readonly string _format;
    private readonly ILogger _logger;
    private readonly IEdgeEventProvider _edgeEventProvider;
    private readonly string _debugLogsPath;
    private volatile bool _httpDebugEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="OmfByteHttpClient"/> class.
    /// </summary>
    /// <param name="configuration">Endpoint configuration.</param>
    /// <param name="dataProtector"><see cref="IEdgeDataProtector"/> instance.</param>
    /// <param name="manifest"><see cref="IApplicationManifest"/> instance.</param>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="compressor"><see cref="ICompressor"/> instance.</param>
    /// <param name="format">Format of the outgoing message.</param>
    /// <param name="debugLogsPath">Full path to directory where egress debug logs should be stored.</param>
    /// <param name="edgeEventProvider">Optional <see cref="IEdgeEventProvider"/> instance.</param>
    public OmfByteHttpClient(
        IEndpointConfiguration configuration,
        IEdgeDataProtector dataProtector,
        IApplicationManifest manifest,
        ILogger logger,
        ICompressor compressor,
        string format,
        string debugLogsPath,
        IEdgeEventProvider edgeEventProvider = null) : base(configuration, dataProtector, manifest, logger)
    {
        ThrowHelper.ThrowIfArgumentNull(manifest, nameof(manifest));

        _logger = logger;
        _compressor = compressor;
        _format = format;
        _debugLogsPath = debugLogsPath;
        _edgeEventProvider = edgeEventProvider;
    }

    public static MessageType GetOmfMessageType(MessageType messageType)
    {
        if (messageType == MessageType.DynamicData || messageType == MessageType.StaticData)
        {
            messageType = MessageType.Data;
        }

        return messageType;
    }

    /// <inheritdoc/>
    public async Task<EndpointResponse> SendMessageAsync(MessageType messageType, byte[] messageBody, MessageAction messageAction, OmfVersion omfVersion, CancellationToken token, PartitionKey? partitionKey = null)
    {
        if (ShouldRecreateHttpClient)
        {
            RecreateHttpClient();
        }

        var omfMessageType = GetOmfMessageType(messageType);

        try
        {
            var messageActionString = GetMessageActionString(messageAction, messageType);
            var operationId = Guid.NewGuid().ToString();
            _httpDebugEnabled = IsHttpDebugEnabled();

            var response = await SendMessageInternalAsync(
                omfMessageType.ToString(),
                messageBody,
                messageActionString,
                operationId,
                omfVersion,
                token,
                partitionKey);

            return await HandleResponseAsync(response, messageType, operationId);
        }
        catch (HttpRequestException httpRequestException)
        {
            RaiseHttpRequestExecutedEvent(messageType, null, null, httpRequestException);

            return new EndpointResponse(ResponseStatusEnum.Fail, $"{nameof(HttpRequestException)}: {httpRequestException.Message}");
        }
        catch (TaskCanceledException taskCanceledException)
        {
            if (!token.IsCancellationRequested)
            {
                RaiseHttpRequestExecutedEvent(messageType, null, null, taskCanceledException);

                return new EndpointResponse(ResponseStatusEnum.Fail, $"{nameof(TaskCanceledException)}: The send operation timed out.");
            }

            throw;
        }
        catch (Exception ex)
        {
            RaiseHttpRequestExecutedEvent(messageType, null, null, ex);

            return new EndpointResponse(ResponseStatusEnum.Fail, $"Send operation failed: {ex.GetExceptionTypeAndMessages()}.");
        }
    }

    private static string GetMessageActionString(MessageAction messageAction, MessageType messageType)
    {
        if (messageAction == MessageAction.Default)
        {
            if (messageType == MessageType.StaticData || messageType == MessageType.Container)
            {
                return UpdateOperationString;
            }

            return CreateOperationString;
        }
        else
        {
            return messageAction.ToString();
        }
    }

    private static string GetDelayResponseMessageAndUpdateDelay(ref TimeSpan? delay)
    {
        if (delay == null)
        {
            return $"The endpoint has requested a delay in sending messages. Messages will be retried in {EndpointResponse.DefaultDelay}.";
        }

        if (delay > EndpointResponse.MaxDelay)
        {
            var oldDelay = delay;
            delay = EndpointResponse.MaxDelay;
            return $"The endpoint has requested a delay in sending messages for {oldDelay.Value}. " +
                $"Messages will be retried in {EndpointResponse.MaxDelay} (maximum delay time).";
        }

        return $"The endpoint has requested a delay in sending messages. Messages will be retried in {delay.GetValueOrDefault(EndpointResponse.DefaultDelay)}";
    }

    private static EndpointResponse CreateTraceOrDebugResponse(ResponseStatusEnum responseStatus, HttpResponseMessage responseMessage, string messageContent) =>
        new(responseStatus, $"{(int)responseMessage.StatusCode} ({responseMessage.StatusCode}):{Environment.NewLine}{messageContent}.");

    private static EndpointResponse CreateDelayRequiredEndpointResponse(HttpResponseMessage responseMessage, string messageContent)
    {
        var delay = responseMessage.Headers.RetryAfter?.Delta;

        if (delay == null && responseMessage.Headers.RetryAfter?.Date != null)
        {
            delay = responseMessage.Headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
        }

        var message = GetDelayResponseMessageAndUpdateDelay(ref delay);

        return new EndpointResponse(ResponseStatusEnum.DelayRequired, messageContent + ". " + message, delay);
    }

    private static EndpointResponse CreateFailedEndpointResponse(HttpResponseMessage responseMessage, string messageContent)
    {
        if (!string.IsNullOrWhiteSpace(messageContent))
        {
            messageContent = "Response message content: " + messageContent;
        }

        return new EndpointResponse(ResponseStatusEnum.Fail,
            $"Response status code does not indicate success: {(int)responseMessage.StatusCode} ({responseMessage.StatusCode}). {messageContent}");
    }

    private bool IsHttpDebugEnabled()
    {
        var debugExpiration = Configuration.DebugExpiration;

        if (debugExpiration != null && ((DateTime)debugExpiration).ToUniversalTime() > DateTime.UtcNow)
        {
            return true;
        }

        return false;
    }

    private async Task<EndpointResponse> HandleResponseAsync(HttpResponseMessage response, MessageType messageType, string operationId)
    {
        switch (response.StatusCode)
        {
            // 1xx
            case HttpStatusCode.Continue:
            case HttpStatusCode.SwitchingProtocols:
                break;

            // 2xx
            case HttpStatusCode.OK:
            case HttpStatusCode.Created:
            case HttpStatusCode.Accepted:
            case HttpStatusCode.NonAuthoritativeInformation:
            case HttpStatusCode.NoContent:
            case HttpStatusCode.ResetContent:
            case HttpStatusCode.PartialContent:
                break;

            // 3xx
            case HttpStatusCode.Ambiguous:
            case HttpStatusCode.Moved:
            case HttpStatusCode.Found:
            case HttpStatusCode.RedirectMethod:
            case HttpStatusCode.NotModified:
            case HttpStatusCode.UseProxy:
            case HttpStatusCode.Unused:
            case HttpStatusCode.RedirectKeepVerb:
                break;

            // 4xx
            case HttpStatusCode.BadRequest:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.BadRequest, response, messageType, operationId);
            case HttpStatusCode.Conflict:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.Conflict, response, messageType, operationId);
            case HttpStatusCode.TooManyRequests:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.DelayRequired, response, messageType, operationId);
            case HttpStatusCode.NotFound:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.NotFound, response, messageType, operationId);
            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.PaymentRequired:
                break;
            case HttpStatusCode.Forbidden:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.Forbidden, response, messageType, operationId);
            case HttpStatusCode.MethodNotAllowed:
            case HttpStatusCode.NotAcceptable:
            case HttpStatusCode.ProxyAuthenticationRequired:
            case HttpStatusCode.RequestTimeout:
            case HttpStatusCode.Gone:
            case HttpStatusCode.LengthRequired:
            case HttpStatusCode.PreconditionFailed:
            case HttpStatusCode.RequestEntityTooLarge:
            case HttpStatusCode.RequestUriTooLong:
            case HttpStatusCode.UnsupportedMediaType:
            case HttpStatusCode.RequestedRangeNotSatisfiable:
            case HttpStatusCode.ExpectationFailed:
            case HttpStatusCode.UpgradeRequired:
                break;

            // 5xx
            case HttpStatusCode.ServiceUnavailable:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.DelayRequired, response, messageType, operationId);
            case HttpStatusCode.InternalServerError:
                if (messageType == MessageType.StaticData)
                {
                    return await CreateEndpointResponseAsync(ResponseStatusEnum.InternalServerError, response, messageType, operationId);
                }

                break;
            case HttpStatusCode.NotImplemented:
                return await CreateEndpointResponseAsync(ResponseStatusEnum.NotImplemented, response, messageType, operationId);
            case HttpStatusCode.BadGateway:
            case HttpStatusCode.GatewayTimeout:
            case HttpStatusCode.HttpVersionNotSupported:
                break;

            default:
                _logger.LogWarning("Response returned a status code that is not supported. Status code: {StatusCode}.", response.StatusCode);
                break;
        }

        if (!response.IsSuccessStatusCode)
        {
            return await CreateEndpointResponseAsync(ResponseStatusEnum.Fail, response, messageType, operationId);
        }

        return await CreateEndpointResponseAsync(ResponseStatusEnum.Success, response, messageType, operationId);
    }

    private async Task<HttpResponseMessage> SendMessageInternalAsync(
        string messageType,
        byte[] messageBody,
        string omfAction,
        string operationId,
        OmfVersion omfVersion,        
        CancellationToken token,
        PartitionKey? partitionKey = null)
    {
        var headers = new Dictionary<string, string>
        {
            { MessageTypeHeaderKey, messageType },
            { MessageFormatString, _format },
            { ActionString, omfAction },
            { OmfVersionString, omfVersion.ToVersionString() },
        };

        if (_compressor != null)
        {
            headers.Add(MessageCompressionHeaderKey, _compressor.Compression);
        }

        if (partitionKey != null && partitionKey.Value != 0)
        {
            headers.Add(MessagePartitionKeyHeaderKey, (byte)partitionKey.Value + "_" + Configuration.ClientId);
        }

        AddCsrfHeader(headers);
        AddAcceptVerbosityHeader(headers);

        if (_httpDebugEnabled)
        {
            await TraceHttpRequestAsync(messageType, headers, messageBody, operationId);
        }

        await SetAuthorizationHeaderAsync(token);

        using var messageContent = BuildContent(messageBody, headers);
#pragma warning disable CA2234 // Pass system uri objects instead of strings
        return await Client.PostAsync(string.Empty, messageContent, token);
#pragma warning restore CA2234 // Pass system uri objects instead of strings
    }

    private async Task<EndpointResponse> CreateEndpointResponseAsync(ResponseStatusEnum responseStatus, HttpResponseMessage responseMessage, MessageType messageType, string operationId)
    {
        if (responseStatus == ResponseStatusEnum.Success)
        {
            return await CreateSuccessfulEndpointResponseAsync(responseMessage, messageType, operationId);
        }

        var messageContent = await responseMessage.Content.ReadAsStringAsync();
        if (_httpDebugEnabled)
        {
            TraceHttpResponse(GetOmfMessageType(messageType).ToString(), responseMessage, messageContent, operationId);
        }

        RaiseHttpRequestExecutedEvent(messageType, responseMessage.StatusCode, messageContent);

        return responseStatus switch
        {
            ResponseStatusEnum.Fail => CreateFailedEndpointResponse(responseMessage, messageContent),
            ResponseStatusEnum.DelayRequired => CreateDelayRequiredEndpointResponse(responseMessage, messageContent),
            _ => CreateTraceOrDebugResponse(responseStatus, responseMessage, messageContent),
        };
    }

    private async Task TraceHttpRequestAsync(string messageType, Dictionary<string, string> headers, byte[] messageBody, string operationId)
    {
        var debugMessageBody = messageBody;
        if (_compressor != null)
        {
            debugMessageBody = await _compressor.DecompressAsync(messageBody);
        }

        var headersContent = string.Join("; ", headers.Select(x => x.Key + "=" + x.Value));
        var logMessageContent = $"{headersContent}{Environment.NewLine}{Environment.NewLine}{Encoding.UTF8.GetString(debugMessageBody)}";

        TraceHttpInteraction(RequestString, messageType, logMessageContent, operationId);
    }

    private void TraceHttpResponse(string messageType, HttpResponseMessage responseMessage, string messageContent, string operationId)
    {
        var content = $"{responseMessage}{Environment.NewLine}{Environment.NewLine}{messageContent}";

        TraceHttpInteraction(ResponseString, messageType, content, operationId);
    }

    private void TraceHttpInteraction(string interactionType, string messageType, string content, string operationId)
    {
        var directory = Path.Combine(_debugLogsPath, messageType);

        try
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var filePath = Path.Combine(directory, $"{DateTime.UtcNow.Ticks}-{operationId}-{interactionType}.txt");
            File.WriteAllText(filePath, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log OMF egress {InteractionType} to the following location: {Location}.", interactionType, directory);
        }
    }

    private async Task<EndpointResponse> CreateSuccessfulEndpointResponseAsync(HttpResponseMessage responseMessage, MessageType messageType, string operationId)
    {
        var successfulResponse = new EndpointResponse(ResponseStatusEnum.Success);
        var successfulMessageContent = string.Empty;

        if (_logger.IsEnabled(LogLevel.Trace) || _httpDebugEnabled)
        {
            successfulMessageContent = await responseMessage.Content.ReadAsStringAsync();
        }

        if (_logger.IsEnabled(LogLevel.Trace))
        {
            successfulResponse = CreateTraceOrDebugResponse(ResponseStatusEnum.Success, responseMessage, successfulMessageContent);
        }

        if (_httpDebugEnabled)
        {
            TraceHttpResponse(GetOmfMessageType(messageType).ToString(), responseMessage, successfulMessageContent, operationId);
        }

        RaiseHttpRequestExecutedEvent(messageType, responseMessage.StatusCode, successfulMessageContent);

        return successfulResponse;
    }

    private void RaiseHttpRequestExecutedEvent(MessageType messageType, HttpStatusCode? statusCode, string content, Exception exception = null)
    {
        if (_edgeEventProvider == null)
        {
            return;
        }

        var egressEvent = new HttpRequestExecutionInfo(
            Configuration.Id,
            Configuration.Endpoint,
            messageType,
            statusCode,
            content,
            exception);

        if (!_edgeEventProvider.EdgeEventChannel.Writer.TryWrite(egressEvent))
        {
            _logger.LogDebug("Failed to write OMF egress event to channel. Event: {Event}.", egressEvent);
        }
    }
}
