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
using AdapterFramework.Data.Framework.Abstractions.Security.Attributes;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// Defines base properties of Endpoint Configuration.
/// </summary>
public interface IEndpointConfiguration : IEquatable<IEndpointConfiguration>
{
    /// <summary>
    /// Configuration entry ID.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Endpoint URL
    /// </summary>
    string Endpoint { get; }

    /// <summary>
    /// User name string used for authentication to the <see cref="Endpoint"/> in combination with <see cref="Password"/>.
    /// </summary>
    string UserName { get; }

    /// <summary>
    /// Password string used for authentication to the <see cref="Endpoint"/> in combination with <see cref="UserName"/>.
    /// </summary>
    [Protected]
    string Password { get; }

    /// <summary>
    /// Client ID for obtaining an authentication token from <see cref="Endpoint"/> in combination with <see cref="ClientSecret"/>.
    /// </summary>
    string ClientId { get; }

    /// <summary>
    /// Client secret for obtaining an authentication token from <see cref="Endpoint"/> in combination with <see cref="ClientId"/>.
    /// </summary>
    [Protected]
    string ClientSecret { get; }

    /// <summary>
    /// Enables logging of each outbound HTTP request from this egress endpoint to disk. The value represents the date and time when this logging should stop. Expected input UTC: "yyyy-mm-ddThh:mm:ssZ", Local: "mm-dd-yyyy hh:mm:ss".
    /// </summary>
    public DateTime? DebugExpiration { get; }

    /// <summary>
    /// URL for obtaining access token for OMF Cloud Services
    /// </summary>
    string TokenEndpoint { get; }

    /// <summary>
    /// Option to disable the target endpoint certificate validation.
    /// </summary>
    bool ValidateEndpointCertificate { get; }
}
