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

namespace AdapterFramework.Data.Framework.Abstractions.Diagnostics;

public class EdgeDiagnosticsEvent
{
    public DateTime Timestamp { get; set; }

    public int ProcessIdentifier { get; set; }

    public DateTime StartTime { get; set; }

    public double WorkingSet { get; set; }

    public double TotalProcessorTime { get; set; }

    public double TotalUserProcessorTime { get; set; }

    public double TotalPrivilegedProcessorTime { get; set; }

    public int ThreadCount { get; set; }

    public int HandleCount { get; set; }

    public double ManagedMemorySize { get; set; }

    public double PrivateMemorySize { get; set; }

    public double PeakPagedMemorySize { get; set; }

    public double StorageTotalSize { get; set; }

    public double StorageFreeSpace { get; set; }
}
