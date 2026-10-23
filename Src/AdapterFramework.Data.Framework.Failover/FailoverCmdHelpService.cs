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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Failover.Configuration;

namespace AdapterFramework.Data.Framework.Failover;

public class FailoverCmdHelpService : CmdHelpServiceBase
{
    public FailoverCmdHelpService()
        : base(EdgeSystemConstants.SystemComponentId)
    {
    }

    public string GetClientFailoverHelpOutput()
    {
        return GetConfigHelpHeader(EdgeSystemConstants.ClientFailoverFacetName) +
               $@"
{nameof(ClientFailoverConfiguration.FailoverGroupId)}                  [Required] ID of the failover group to register in the failover endpoint for the adapter instance.
{nameof(ClientFailoverConfiguration.Name)}                             [Optional] User-friendly name of the failover group.
{nameof(ClientFailoverConfiguration.Description)}                      [Optional] Description of the failover group.
{nameof(ClientFailoverConfiguration.FailoverTimeout)}                  [Required] Failover timeout value of the failover group.
{nameof(ClientFailoverConfiguration.Mode)}                             [Required] The failover mode used for the adapter instance.
{nameof(ClientFailoverConfiguration.Endpoint)}                         [Required] URL of the failover endpoint.
{nameof(ClientFailoverConfiguration.UserName)}                         [Optional group 1] User name used for BASIC authentication to failover endpoint.
{nameof(ClientFailoverConfiguration.Password)}                         [Optional group 1] Password used for BASIC authentication to failover endpoint.
{nameof(ClientFailoverConfiguration.ClientId)}                         [Optional group 2] Client ID used for Bearer authentication to failover endpoint.
{nameof(ClientFailoverConfiguration.ClientSecret)}                     [Optional group 2] Client Secret used for Bearer authentication to failover endpoint.
{nameof(ClientFailoverConfiguration.TokenEndpoint)}                    [Optional] URL of failover endpoint's token service.
{nameof(ClientFailoverConfiguration.ValidateEndpointCertificate)}      [Optional] If true, endpoint certificate will be validated (recommended). If false, any endpoint certificate will be accepted. AVEVA strongly recommends using disabled endpoint certificate validation for testing purposes only.

Note: Only one optional group pairing must be specified.
Note: To use Kerberos authentication, do not specify any group.";
    }
}
