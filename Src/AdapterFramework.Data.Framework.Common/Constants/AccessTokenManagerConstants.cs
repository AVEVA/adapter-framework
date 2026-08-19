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
namespace AdapterFramework.Data.Framework.Common.Constants;

public static class AccessTokenManagerConstants
{
    public const string ClientCredentialsString = "client_credentials";
    public const string ClientIdString = "client_id";
    public const string ClientSecretString = "client_secret";
    public const string GrantTypeString = "grant_type";
    public const string IdentityResourceSuffix = "Identity/Connect/Token";

    public const int AccessTokenExpiryDelta = 30;

    public const string BearAuthConfigLocation = ".well-known/openid-configuration";
    public const string BearAuthConfigLocationWithSlash = "Identity/" + BearAuthConfigLocation;
}
