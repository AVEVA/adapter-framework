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
using System.Runtime.InteropServices;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Extensions;
using static AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants;

namespace AdapterFramework.Data.Framework.Common.Helpers;

public abstract class CmdHelpServiceBase
{
    protected const string Spacer = "  ";
    private static readonly bool _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    protected CmdHelpServiceBase(string componentId)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(componentId, nameof(componentId));

        ComponentId = componentId;
    }

    protected string ComponentId { get; }

    public static string GetConfigurationCommandBase() => $"{GetApplicationLaunchCommand(CmdUtilityName)} {ConfigurationKeyword}";

    public static string GetHelpCommandBase() => $"{GetApplicationLaunchCommand(CmdUtilityName)} {HelpKeyword}";

    public static string GetAdministrationCommandBase() => $"{GetApplicationLaunchCommand(CmdUtilityName)} {AdministrationKeyword}";

    public static string GetLaunchCommand()
    {
        return _isWindows
            ? ".\\"
            : "./";
    }

    public static string GetApplicationLaunchCommand(string utilityName)
    {
        return _isWindows
            ? $"{GetLaunchCommand()}{utilityName}.exe"
            : $"{GetLaunchCommand()}{utilityName}";
    }

    protected string GetLoggingHelp()
    {
        return GetConfigHelpHeader(LoggingFacetName) +
               $@"
{nameof(LoggerConfiguration.LogLevel)}                    [Optional] Desired log level settings. Options: Trace, Debug, Information, Warning, Error, Critical, None.
{nameof(LoggerConfiguration.LogFileSizeLimitBytes)}       [Optional] Maximum size in bytes of log files that the service will create for this component. Must be no less than {LoggerConfiguration.MinLogFileSizeLimitBytes}.
{nameof(LoggerConfiguration.LogFileCountLimit)}           [Optional] Maximum number of log files that the service will create for this component. Must be a positive integer.
";
    }

    protected string GetConfigHelpHeader(string facetName)
    {
        return $@"
---------------------------------------------------------------------------------------------------------
Component {ComponentId} command-line facet => '{facetName}'
---------------------------------------------------------------------------------------------------------";
    }

    protected string GetManagementHelpHeader(string facetName)
    {
        return $@"
---------------------------------------------------------------------------------------------------------
{ComponentId} command-line facet => '{facetName}'
---------------------------------------------------------------------------------------------------------";
    }
}
