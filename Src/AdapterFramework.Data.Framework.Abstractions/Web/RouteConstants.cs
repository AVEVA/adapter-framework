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
namespace AdapterFramework.Data.Framework.Abstractions.Web;

/// <summary>
/// HTTP route constants
/// </summary>
public static class RouteConstants
{
    public const string AdministrationRoute = "Administration";
    public const string ConfigurationRoute = "Configuration";
    public const string DiagnosticsRoute = "Diagnostics";
    public const string ManagementRoute = "Management";
    public const string HelpRoute = "Help";
    public const string ApiRoute = "api";
    public const string ApiVersion1String = "v1";
    public const string BaseRoute = ApiRoute + "/" + ApiVersion1String + "/";
    public const string BaseConfigurationRoute = BaseRoute + ConfigurationRoute + "/";
    public const string BaseAdministrationRoute = BaseRoute + AdministrationRoute + "/";
    public const string BaseDiagnosticsRoute = BaseRoute + DiagnosticsRoute + "/";
    public const string BaseHelpRoute = BaseRoute + HelpRoute + "/";
    public const string BaseManagementRoute = BaseRoute + ManagementRoute + "/";

    public const string ProductInformationTopic = "ProductInformation";
    public const string SystemTopic = "System";
    public const string SecretsTopic = "Secrets";
    public const string FailoverStateTopic = "FailoverState";
}
