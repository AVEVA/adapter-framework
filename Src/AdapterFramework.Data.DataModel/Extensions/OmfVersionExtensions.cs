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

namespace AdapterFramework.Data.DataModel.Extensions;

public static class OmfVersionExtensions
{
    public static string ToVersionString(this OmfVersion version) => version switch
    {
        OmfVersion.Omf12 => "1.2",
        OmfVersion.Omf13 => "1.3",
        OmfVersion.Omf20 => "2.0",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };
}
