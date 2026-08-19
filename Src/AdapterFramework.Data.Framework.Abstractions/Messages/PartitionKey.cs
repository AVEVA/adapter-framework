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
namespace AdapterFramework.Data.Framework.Abstractions.Messages;

/// <summary>
/// A partition key that will be sent as a message header to the OMFIngress Service, 
/// which uses the key to route messages to a certain partition of the Azure EventHub used by the service. 
/// Different keys distribute messages across different partitions, which can help with parallel processing of messages. 
/// </summary>
public enum PartitionKey
{
    Key1 = 1,
    Key2 = 2,
    Key3 = 3,
    Key4 = 4,
    Key5 = 5,
    Key6 = 6,
    Key7 = 7,
    Key8 = 8,
    Key9 = 9,
    Key10 = 10,
    Key11 = 11,
    Key12 = 12,
    Key13 = 13,
    Key14 = 14,
    Key15 = 15,
    Key16 = 16,
}
