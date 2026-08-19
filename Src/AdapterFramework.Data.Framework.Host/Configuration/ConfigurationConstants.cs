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
namespace AdapterFramework.Data.Framework.Host.Configuration;

internal static class ConfigurationConstants
{
    internal const string ApplicationDataDirectoryKey = "ApplicationSettings:ApplicationDataDirectory";
    internal const string ApplicationPortKey = "ApplicationSettings:Port";
    internal const string DeviceNameKey = "ApplicationSettings:DeviceName";
    internal const string DefaultAppSettingsFileName = "appsettings.json";
    internal const string AppSettingsFileTemplate = "appsettings.{0}.json";
    internal const string DefaultInstanceId = "1";
    internal const string ApplicationDataDirectoryParameter = "--applicationdatadirectory:";
    internal const string ApplicationPortParameter = "--port:";
    internal const string DeviceNameParameter = "--devicename:";
    internal const string InstanceParameter = "--instance:";

    internal const string BaseUri = "http://127.0.0.1:{0}";

    internal const int MinPortNumber = 1024;
    internal const int MaxPortNumber = 49151;
}
