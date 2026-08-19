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
namespace AdapterFramework.Data.Framework.Abstractions.Constants;

/// <summary>
/// This class holds various constants used across the Edge System.
/// </summary>
public static class EdgeSystemConstants
{
    public const string AdapterFrameworkDllNameBase = "AdapterFramework.Data.";
    public const string AdapterDllNameBase = AdapterFrameworkDllNameBase + "Adapter";
    public const string AbstractionsDllFullName = AdapterFrameworkDllNameBase + "Framework.Abstractions";
    public const string StorageComponentDllFullName = AdapterFrameworkDllNameBase + "Storage.EDS.Component";
    public const string EgressComponentDllFullName = AdapterFrameworkDllNameBase + "Framework.EgressComponent";
    public const string FailoverServiceDllFullName = AdapterFrameworkDllNameBase + "Framework.Failover";
    public const string SystemLogSourceName = "System";
    public const string SystemComponentId = "System";
    public const string SystemComponentIdUpper = "SYSTEM";
    public const string StorageComponentId = "Storage";
    public const string LoggingFacetName = "Logging";
    public const string HealthEndpointsFacetName = "HealthEndpoints";
    public const string DataFiltersFacetName = "DataFilters";
    public const string SchedulesFacetName = "Schedules";
    public const string AdapterComponentTypePlaceholder = "AdapterComponentTypePlaceholder";

    public const string ResetFunctionName = "Reset";
    public const string SystemLevelResetMarkerFile = "SystemLevelReset";

    public const string ComponentsFacetName = "Components";
    public const string PortFacetName = "Port";
    public const string GeneralFacetName = "General";

    public const string RunAsServiceRoute = "--service";

    public const string DllExtension = ".dll";
    public const string AddComponentMethodName = "AddComponent";
    public const string UseComponentMethodName = "UseComponent";

    public const string StorageOldComponentType = "EDS.Component";
    public const string StorageComponentType = "Storage";
    public const string OmfEgressComponentType = "OmfEgress";
    public const string DataEndpointsFacetName = "DataEndpoints";
    public const string OmfEgressComponentId = "OmfEgress";
    public const string BufferingFacetName = "Buffering";
    public const string BuffersDirectoryName = "Buffers";
    public const string SecretsFacetName = "Secrets";

    public const int MinConfigArgumentsCount = 1;
    public const string UnsupportedOperationString = "Operation '{0}' is not supported on '{1}' configuration.";
    public const string NullOrEmptyErrorMessage = "{0} cannot be null or empty.";

    public const string ConfigurationDirectoryName = "Configuration";
    public const string EdgeSystemDirectoryName = "EdgeDataStore";
    public const string AdapterFrameworkDirectoryName = "AdapterFramework";
    public const string LoggingDirectoryName = "Logs";
    public const string RemovedDirectoryName = "Removed";

    public const string ConfigurationInvalidMessage = "The '{ConfigName}' configuration is invalid. A configuration with default parameters will be used: {Errors}";
    public const string OriginalConfigurationInvalidMessage = "The original '{ConfigName}' configuration is invalid and will be moved to Removed folder: {Errors}";
    public const string FailedToMoveInvalidConfigurationMessage = "Failed to move the invalid '{ConfigName}' configuration to Removed folder: {Errors}";
    public const string FailedToSaveConfigurationMessage = "Failed to save the {ConfigName} configuration: {Errors}";
    public const string CertificateExpirationMessage = "The server certificate with subject {Subject} expired on {ExpirationDate}. Please renew or replace it with a new certificate.";

    public const string ConfigurationKeyword = "Configuration";
    public const string AdministrationKeyword = "Administration";
    public const string ManagementComponentId = "Management";
    public const string HelpKeyword = "Help";
    public const string CmdUtilityName = "edgecmd";

    public const string SeparatorUnderscore = "_";
    public const string SeparatorForwardSlash = "/";
    public const char SeparatorSlashCharacter = '/';
    public const string SeparatorHyphen = "-";

    public const string ApplicationName = "Edge Data Store";

    public const string AdaptersHealthRootId = "Adapters";
    public const string AdaptersHealthRootName = "Adapters";
    public const string AdaptersHealthRootDescription = "Collection of Adapter Assets";

    public const string EdsAdaptersTypePrefix = "EDS";

    public const string EdgeDataStoreHealthRootId = "EdgeDataStores";
    public const string EdgeDataStoreHealthRootName = "Edge Data Stores";
    public const string EdgeDataStoreHealthRootDescription = "Collection of Edge Data Store Assets";
    public const string EdgeDataStoreHealthType = "EdgeDataStore";
    
    public const string UnknownMachine = "UnknownMachineName";

    public const string AdapterTypeString = "AdapterType";
    public const string DataSourceString = "DataSource";
    public const string DataSelectionString = "DataSelection";

    public const string LogMessagePrefixPlaceholder = "Prefix";

    public const int MaximumStreamIdLengthNoPrefix = 1900;
    public const int MaximumStreamIdPrefixLength = 100;
    public const int MaximumEventIdPrefixLength = 100;
    public const int MaximumComponentIdLength = 99;
    public const int OneCharacter = 1;
    public const string MaximumIdentifierLengthExceededError = "{0}: The specified value is longer than the allowed maximum length of {1} characters. If the string contains invalid OMF characters, the replacement for these characters may have also caused the maximum length to be exceeded.";
    public const string MaximumStringLengthExceededError = "{0}: The specified value is longer than the allowed maximum length of {1} characters.";

    public const int DefaultDataBulkTime = 1000;

    public const string SecretIdPlaceholderPattern = "^{{[^{}]+}}$";
    public const string DateTimeRegexPattern = "^(\\d{4})-(0[1-9]|1[0-2])-(0[1-9]|[12][0-9]|3[01])[Tt]([01][0-9]|2[0-3]):([0-5][0-9]):([0-5][0-9]|60)(\\.[0-9]+)?([Zz]|(\\+|-)([01][0-9]|2[0-3]):([0-5][0-9]))?$";
    public const string DateTimePatternDescription = "UTC time 2023-01-29T12:43:00Z";

    // Following timespan regex allows value between ranges (hh:mm:ss: 00:00:00 - 23:59:59) or (sss: 0-86399) as user input
    public const string NonNegativeTimespanRegexPattern = "(^([0-9]{1}|(?:0[0-9]|1[0-9]|2[0-3])+):([0-5]?[0-9])(?::([0-5]?[0-9])(?:.(\\d{1,9}))?)?$)|(^[0-9]{1,5}$)";
    
    // Following timespan regex allows value between ranges (hh:mm:ss: 00:00:01 - 23:59:59) or (sss: 1-86399) as user input
    public const string PositiveNonZeroTimespanRegexPattern = "(^((?!00:00:00)([0-9]{1}|(?:0[0-9]|1[0-9]|2[0-3])+):([0-5]?[0-9])(?::([0-5]?[0-9])(?:.(\\d{1,9}))?)?)$)|(^(?!0)([0-9]{1})$)|(^(?!00)([0-9]{2})$)|(^(?!000)([0-9]{3})$)|(^(?!0000)([0-9]{4})$)|(^(?!00000)([0-9]{5})$)";

    public const string ClientFailoverFacetName = "ClientFailover";
    public const string FailoverStateFacetName = "FailoverState";
    public const string FailoverComponentType = "Failover";

    public const string BasicAuth = "Basic";
    public const string BearerAuth = "Bearer";
    public const string NegotiateAuth = "Negotiate";

    public const string Omf12Version = "1.2";
    public const string Omf13Version = "1.3";
}
