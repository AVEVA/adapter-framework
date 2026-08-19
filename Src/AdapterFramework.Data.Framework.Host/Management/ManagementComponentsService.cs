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
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Common.Helpers;

namespace AdapterFramework.Data.Framework.Host.Management;

public class ManagementComponentsService : CmdHelpServiceBase
{
    /// <summary>
    /// Management command-line configuration service.
    /// </summary>
    public ManagementComponentsService()
        : base(EdgeSystemConstants.ManagementComponentId)
    {
    }

    public string GetSecretsHelpOutput()
    {
        return GetManagementHelpHeader(EdgeSystemConstants.SecretsFacetName) +
               $@"
{nameof(ManagedSecretConfiguration.Id)}                           [Required] Id of configuration to be added, edited, or removed.
{nameof(ManagedSecretConfiguration.Description)}                  [Optional] Description of the secret.
{nameof(ManagedSecretConfiguration.ExpirationDate)}               [Optional] Expiration date of the secret.
{nameof(ManagedSecretConfiguration.Value)}                        [Required] The secret value.
";
    }
}
