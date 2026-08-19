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
using System.Collections.Generic;
using System.Text;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Host.Configuration;

public class SystemCmdConfigService : CmdHelpServiceBase
{
    private const string NoAdapterAvailableString = "NoAdapterComponentAvailable";
    private readonly string _availableComponentsString;

    /// <summary>
    /// System command-line configuration service
    /// </summary>
    /// <param name="componentId">Component ID for which is the service created.</param>
    /// <param name="registeredAdapterTypes">Enumerable of <see cref="IEdgeAdapter"/> component types that were registered into DI container.</param>
    /// <param name="applicationManifest"><see cref="IApplicationManifest"/> service instance.</param>
    public SystemCmdConfigService(string componentId, IEnumerable<string> registeredAdapterTypes, IApplicationManifest applicationManifest)
        : base(componentId)
    {
        ThrowHelper.ThrowIfArgumentNull(registeredAdapterTypes, nameof(registeredAdapterTypes));
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        var (availableComponents, _) = GetAvailableComponentsString(registeredAdapterTypes, applicationManifest);

        _availableComponentsString = availableComponents;
    }

    public string GetLoggingHelpOutput()
    {
        return GetLoggingHelp();
    }

    public string GetHealthEndpointsHelpOutput()
    {
        return GetConfigHelpHeader(EdgeSystemConstants.HealthEndpointsFacetName) +
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

    public string GetGeneralHelpOutput()
    {
        return GetConfigHelpHeader(EdgeSystemConstants.GeneralFacetName) +
               $@"
{nameof(GeneralConfiguration.EnableDiagnostics)}              [Optional] Enable application diagnostics.
{nameof(GeneralConfiguration.MetadataLevel)}                  [Optional] Defines amount of metadata sent to OMF endpoints. Options: None, Low, Medium, High.
{nameof(GeneralConfiguration.HealthPrefix)}                   [Optional] Prefix to use for health and diagnostics stream and asset IDs.
{nameof(GeneralConfiguration.IncludeSourceProperties)}        [Optional] Defines which source properties to include in array format (e.g. [""Minimum"",""Maximum"",""Uom""]). Property types: None, Description, Minimum, Maximum, Uom, Interpolation, All
";
    }

    public string GetComponentsHelpOutput()
    {
        return GetConfigHelpHeader(EdgeSystemConstants.ComponentsFacetName) +
               $@"
{nameof(EdgeComponentConfig.ComponentId)}                    [Required] ID of the hosted component.
{nameof(EdgeComponentConfig.ComponentType)}                  [Required] Type of the hosted component. Valid component types: {_availableComponentsString}.
";
    }

    public string GetBufferingHelpOutput()
    {
        {
            return GetConfigHelpHeader(EdgeSystemConstants.BufferingFacetName) +
                   $@"
{nameof(BufferingConfiguration.BufferLocation)}                 [Required] Location of the on-disk buffers.
{nameof(BufferingConfiguration.MaxBufferSizeMB)}                [Optional] Maximum size of the on-disk or in-memory buffers.
{nameof(BufferingConfiguration.EnablePersistentBuffering)}      [Optional] Enable or disable on-disk buffering.
{nameof(BufferingConfiguration.MaxDataBulkTime)}                [Optional] Maximum time before batched messages get sent.
";
        }
    }

    private static (string AvailableComponents, string ExampleAdapterType) GetAvailableComponentsString(IEnumerable<string> registeredAdapterTypes, IApplicationManifest applicationManifest)
    {
        var availableComponentTypesBuilder = new StringBuilder();

        availableComponentTypesBuilder.Append(applicationManifest.IsEdgeDataStore ? EdgeSystemConstants.StorageComponentType : EdgeSystemConstants.OmfEgressComponentType);
        var exampleAdapterType = NoAdapterAvailableString;

        foreach (var registeredAdapterType in registeredAdapterTypes)
        {
            exampleAdapterType = registeredAdapterType;
            availableComponentTypesBuilder.Append(", ");
            availableComponentTypesBuilder.Append(registeredAdapterType);
        }

        return (availableComponentTypesBuilder.ToString(), exampleAdapterType);
    }
}
