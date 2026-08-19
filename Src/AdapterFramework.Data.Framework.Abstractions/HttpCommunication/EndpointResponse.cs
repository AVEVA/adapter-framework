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

namespace AdapterFramework.Data.Framework.Abstractions.HttpCommunication;

/// <summary>
/// An enum for possible endpoint response statuses.
/// </summary>
public enum ResponseStatusEnum
{
    /// <summary>
    /// The message was processed without fail.
    /// </summary>
    Success,

    /// <summary>
    /// The endpoint received the message, however the message was invalid and not processed
    /// </summary>
    BadRequest,

    /// <summary>
    /// The OMF message failed to be sent to the endpoint.
    /// </summary>
    Fail,

    /// <summary>
    /// The endpoint received the message but found a conflict with existing information while processing.
    /// Some parts of the message may have been processed but not all.
    /// </summary>
    Conflict,

    /// <summary>
    /// The client has been sending too many requests or the server is overloaded and must retry the request
    /// at a future time.
    /// </summary>
    DelayRequired,

    /// <summary>
    /// PI Web API specific return code where a container wasn't found for data values included in a message.
    /// </summary>
    NotFound,

    /// <summary>
    /// Return code indicating that the server failed to process entire message due to unhandled exception or
    /// underlying storage problems.
    /// </summary>
    InternalServerError,

    /// <summary>
    /// PI Web API specific return code where message contains OMF feature not supported on the server.
    /// </summary>
    NotImplemented,

    /// <summary>
    /// PI Web API specific return code where user account doesn't have permissions to write data to or edit PI Point.
    /// </summary>
    Forbidden,
}

/// <summary>
/// A struct that contains the necessary information to determine the status of the message that was sent.
/// </summary>
public readonly struct EndpointResponse : IEquatable<EndpointResponse>
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxDelay = TimeSpan.FromHours(1);
    
    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointResponse"/> struct.
    /// </summary>
    /// <param name="responseStatus">The response status given by the endpoint.</param>
    /// <param name="message">The message that was received from the endpoint.</param>
    /// <param name="delay">How long to delay sending the next message.</param>
    public EndpointResponse(ResponseStatusEnum responseStatus, string message = null, TimeSpan? delay = null)
    {
        ResponseStatus = responseStatus;
        Message = message;
        Delay = delay.GetValueOrDefault(DefaultDelay);
    }

    /// <summary>
    /// Gets an enum containing the status of the OMF message sent.
    /// </summary>
    public ResponseStatusEnum ResponseStatus { get; }

    /// <summary>
    /// Gets a (error) message that was received from the endpoint.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the delay required.
    /// </summary>
    public TimeSpan Delay { get; }

    public static bool operator ==(EndpointResponse left, EndpointResponse right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EndpointResponse left, EndpointResponse right)
    {
        return !(left == right);
    }

    public bool Equals(EndpointResponse other)
    {
        return ResponseStatus.Equals(other.ResponseStatus) &&
               Message == other.Message &&
               Delay.Equals(other.Delay);
    }

    public override bool Equals(object obj)
    {
        if (obj is EndpointResponse otherEndpointResponse)
        {
            return Equals(otherEndpointResponse);
        }

        return false;
    }

    public override int GetHashCode() => HashCode.Combine(ResponseStatus, Message, Delay);
}
