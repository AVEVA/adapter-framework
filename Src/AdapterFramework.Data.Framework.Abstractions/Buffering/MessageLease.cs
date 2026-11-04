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
namespace AdapterFramework.Data.Framework.Abstractions.Buffering;

/// <summary>
/// Reservation of the head message of an <see cref="ILeasableMessageQueue{TMessage}"/>.
/// </summary>
/// <typeparam name="TMessage">Specifies the type of the leased message.</typeparam>
public sealed class MessageLease<TMessage>
{
    /// <summary>
    /// Creates a new instance of the <see cref="MessageLease{TMessage}"/> class.
    /// </summary>
    /// <param name="message">The leased message.</param>
    /// <param name="token">Token identifying this lease; a lease with a stale token cannot be completed.</param>
    public MessageLease(TMessage message, long token)
    {
        Message = message;
        Token = token;
    }

    /// <summary>
    /// Gets the leased message.
    /// </summary>
    public TMessage Message { get; }

    /// <summary>
    /// Gets the token identifying this lease.
    /// </summary>
    public long Token { get; }
}
