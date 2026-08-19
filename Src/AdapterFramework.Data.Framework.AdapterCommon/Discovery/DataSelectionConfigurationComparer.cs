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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Extensions;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.AdapterCommon.Tests.Discovery")]
namespace AdapterFramework.Data.Framework.AdapterCommon.Discovery;

internal class DataSelectionConfigurationComparer<TSelection> : IEqualityComparer<TSelection> where TSelection : IDataSelectionConfiguration
{
    public bool Equals(TSelection dataSelectionItemA, TSelection dataSelectionItemB)
    {
        ThrowHelper.ThrowIfArgumentNull(dataSelectionItemA, nameof(dataSelectionItemA));
        ThrowHelper.ThrowIfArgumentNull(dataSelectionItemB, nameof(dataSelectionItemB));

        return dataSelectionItemA.StreamId.Equals(dataSelectionItemB.StreamId, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(TSelection dataSelectionItem)
    {
        return StringComparer.OrdinalIgnoreCase.GetHashCode(dataSelectionItem.StreamId);
    }
}
