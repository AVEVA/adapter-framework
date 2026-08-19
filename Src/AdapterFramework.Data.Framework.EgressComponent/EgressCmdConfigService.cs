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
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Common.Helpers;

namespace AdapterFramework.Data.Framework.EgressComponent;

public class EgressCmdConfigService : CmdHelpServiceBase
{
    public EgressCmdConfigService(string componentId)
        : base(componentId)
    {
    }

    public string GetLoggingHelpOutput()
    {
        return GetLoggingHelp();
    }

    public string GetEgressEndpointsHelpOutput()
    {
        return GetConfigHelpHeader(EgressEndpointConfiguration.ConfigName) +
    $@"
{nameof(EndpointConfigurationBase.Id)}                           [Optional] Id of existing configuration to be edited of removed.
{nameof(EndpointConfigurationBase.Endpoint)}                     [Required] URL of OMF destination
{nameof(EndpointConfigurationBase.UserName)}                     [Optional group 1]  User name used for BASIC authentication to OMF endpoint.
{nameof(EndpointConfigurationBase.Password)}                     [Optional group 1]  Password used for BASIC authentication to OMF endpoint.
{nameof(EndpointConfigurationBase.ClientId)}                     [Optional group 2]  Client ID used for Bearer authentication to OMF endpoint.
{nameof(EndpointConfigurationBase.ClientSecret)}                 [Optional group 2]  Client Secret used for Bearer authentication to OMF endpoint.
{nameof(EndpointConfigurationBase.DebugExpiration)}              [Optional] Enables logging of HTTP traffic (requests and responses) until the configured time is reached.
{nameof(EndpointConfigurationBase.TokenEndpoint)}                [Optional] URL of OMF destination's token service.
{nameof(EndpointConfigurationBase.ValidateEndpointCertificate)}  [Optional] If true, endpoint certificate will be validated (recommended). If false, any endpoint certificate will be accepted. AVEVA strongly recommends using disabled endpoint certificate validation for testing purposes only.

Note: Only one optional group pairing must be specified.
Note: To use Kerberos authentication, do not specify any group.
";
    }
}
