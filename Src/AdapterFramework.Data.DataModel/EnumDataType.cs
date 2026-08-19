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
using AdapterFramework.Data.DataModel.Enum;

namespace AdapterFramework.Data.DataModel;

/// <summary>
/// An object representing an enum OMF Type message.
/// </summary>
public class EnumDataType : DataType
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EnumDataType"/> that includes the <paramref name="typeId"/> and <paramref name="enumeration"/>.
    /// </summary>
    /// <param name="typeId">ID of the type.</param>
    /// <param name="enumeration">The enum in array format.</param>
    /// <param name="name">Friendly name.</param>
    public EnumDataType(string typeId, TypeEnumField enumeration, string name = null)
    {
        Id = typeId;
        Enum = enumeration;
        Name = name;
    }
}
