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
using System;
using System.Globalization;

namespace AdapterFramework.Data.Framework.Tests.Helper;

public static class TestOmfGenerator
{
    private const string DefaultTypeId = "ExampleTypeId";
    private const string DefaultVersion = "1.0.0.0";
    private const string DefaultContainerId = "ContainerId";

    public static string GetTypeMessage(string typeId = DefaultTypeId, string typeVersion = DefaultVersion)
    {
        return "[" +
                    "{" +
                        "\"id\": \"" + typeId + "\"," +
                        "\"version\": \"" + typeVersion + "\"," +
                        "\"type\": \"object\"," +
                        "\"classification\": \"dynamic\"," +
                        "\"properties\": {" +
                            "\"Time\": {" +
                            "\"type\": \"string\"," +
                            "\"format\": \"date-time\"," +
                            "\"isindex\": true" +
                            "}," +
                            "\"" + "Temperature" + "\": {" +
                            "\"type\": \"number\"" +
                            "}," +
                            "\"" + "Pressure" + "\": {" +
                            "\"type\": \"number\"" +
                            "}" +
                        "}" +
                    "}" +
                "]";
    }

    public static string GetContainerMessage(string containerId = DefaultContainerId, string typeId = DefaultTypeId, string typeVersion = DefaultVersion)
    {
        return "[" +
                    "{" +
                        "\"id\": \"" + containerId + "\"," +
                        "\"typeid\": \"" + typeId + "\"," +
                        "\"typeVersion\": \"" + typeVersion + "\"" +
                    "}" +
               "]";
    }

    public static string GetDataMessage(string containerId = DefaultContainerId, int tempVal = 32, double pressureVal = 100.1)
    {
        return "[{" +
                   "\"containerid\": \"" + containerId + "\"," +
                   "\"values\": [{" +
                        "\"Time\": \"" + DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fffzzz", CultureInfo.InvariantCulture) + "\"," +
                            "\"" + "Temperature" + "\": " + tempVal + "," +
                            "\"" + "Pressure" + "\": " + pressureVal +
                   "}]" +
               "}]";
    }
}
