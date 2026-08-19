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
using System.Diagnostics.CodeAnalysis;
using System.Net;
using AdapterFramework.Data.DataModel;

namespace AdapterFramework.Data.Framework.Abstractions.Events;

[SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Event payload stores endpoint URL as serialized text for compatibility.")]
[SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Event payload stores endpoint URL as serialized text for compatibility.")]
public class HttpRequestExecutionInfo : IEdgeEvent
{
    /// <summary>
    /// Represents OMF Egress <see cref="HttpRequestExecutionInfo"/> event resulting from OMF message send operation.
    /// </summary>
    /// <param name="endpointId">OMF Egress endpoint ID.</param>
    /// <param name="endpointUrl">OMF Egress endpoint URL.</param>
    /// <param name="messageType">OMF message type <see cref="MessageType"/>.</param>
    /// <param name="httpStatusCode">HTTP response message content received from the server if any.</param>
    /// <param name="responseContent">HTTP response status code if any.</param>
    /// <param name="exception">HTTP client exception if any.</param>
    public HttpRequestExecutionInfo(
        string endpointId,
        string endpointUrl,
        MessageType messageType,
        HttpStatusCode? httpStatusCode,
        string responseContent,
        Exception exception = null)
    {
        EndpointId = endpointId;
        EndpointUrl = endpointUrl;
        MessageType = messageType;
        HttpStatusCode = httpStatusCode;
        ResponseContent = responseContent;
        Exception = exception;
    }

    /// <summary>
    /// Gets the type of edge event. For <see cref="HttpRequestExecutionInfo"/>, this is always <see cref="EdgeEventType.Egress"/>.
    /// </summary>
    public EdgeEventType EventType => EdgeEventType.Egress;

    /// <summary>
    /// Gets the UTC timestamp when the event was created.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// OMF Egress endpoint ID.
    /// </summary>
    public string EndpointId { get; }

    /// <summary>
    /// OMF Egress endpoint URL.
    /// </summary>
    public string EndpointUrl { get; }

    /// <summary>
    /// OMF message type <see cref="MessageType"/>.
    /// </summary>
    public MessageType MessageType { get; }

    /// <summary>
    /// HTTP response message content received from the server.
    /// </summary>
    public string ResponseContent { get; }

    /// <summary>
    /// HTTP response status code.
    /// </summary>
    public HttpStatusCode? HttpStatusCode { get; }

    /// <summary>
    /// HTTP client exception.
    /// </summary>
    public Exception Exception { get; }

    public override string ToString()
    {
        return $@"
{nameof(EndpointId)}: {EndpointId}
{nameof(EndpointUrl)}: {EndpointUrl}
{nameof(MessageType)}: {MessageType}
{nameof(HttpStatusCode)}: {(HttpStatusCode == null ? string.Empty : (int)HttpStatusCode + " (" + HttpStatusCode + ")")}
{nameof(ResponseContent)}: {ResponseContent}
{nameof(Exception)} Type: {Exception?.GetType()} {nameof(Exception)} Message: {Exception?.Message}";
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(EndpointId, EndpointUrl, MessageType, ResponseContent, HttpStatusCode, Exception);
    }
}
