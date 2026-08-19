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
namespace AdapterFramework.Data.Framework.Common.HttpCommunication;

/// <summary>
/// Defines a list of constants.
/// </summary>
public static class EndpointManagerConstants
{
    #region URLs

    public const string ResourceAddressString = "https://pihomemain.onmicrosoft.com/ocsapi";
    public const string AuthenticationRequestUriString = "https://login.microsoftonline.com/";
    public const string AuthenticationUriSuffix = "/oauth2/token";

    public const string OcsApiPath = "api/omf";
    public const string EdsApiPath = "edge/omf";
    public const string ApiString = "api";

    #endregion

    #region Headers and Form Fields

    public const string AuthorizationHeaderName = "Authorization";
    public const string AuthenticationHeaderName = "Authentication";
    public const string OcsBaseAddressString = "historianmain.osipi.com";
    public const string AcceptVerbosityString = "Accept-Verbosity";
    public const string AcceptString = "Accept";
    public const string MessageFormatString = "messageformat";
    public const string ActionString = "action";
    public const string OmfVersionString = "omfversion";

    public const string VerboseResponseString = "verbose";
    public const string ResponseFormatString = "application/json";
    public const string VerboseMessageFormatString = "verbose json";

    public const string AccessTokenString = "access_token";
    public const string BearerAuthorizationTypeString = "Bearer";
    public const string BasicAuthorizationType = "Basic";

    public const string ResourceString = "resource";

    public const string MessageTypeHeaderKey = "messagetype";
    public const string MessageCompressionHeaderKey = "compression";
    public const string MessagePartitionKeyHeaderKey = "partitionkey";

    public const string CreateOperationString = "create";
    public const string UpdateOperationString = "update";

    public const string OmfVersionNumberString = "1.2";

    public const string CsrfKey = "X-Requested-With";
    public const string EdsCsrfValue = "AdapterFramework.EdgeDataStore";
    public const string AdapterCsrfValue = "AdapterFramework.Adapter";

    public const string AcceptVerbosityKey = "Accept-Verbosity";
    public const string AcceptVerbosityValue = "verbose";

    #endregion
}
