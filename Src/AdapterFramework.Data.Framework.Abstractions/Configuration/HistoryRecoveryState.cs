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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

public class HistoryRecoveryState : EdgeConfigurationBase
{
    [Id]
    public string Id { get; set; }

    [Required]
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    [ReadOnly(true)]
    public DateTime? Checkpoint { get; set; }
    
    [ReadOnly(true)]
    public int Progress { get; set; }

    [ReadOnly(true)]
    public int Items { get; set; }

    [ReadOnly(true)]
    public long RecoveredEvents { get; set; }

    [ReadOnly(true)]
    public OperationStatus Status { get; set; }

    [ReadOnly(true)]
    public string Errors { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString();
        }

        var time = DateTime.UtcNow;

        StartTime ??= time;
        EndTime ??= time;

        if (StartTime >= EndTime)
        {
            yield return new ValidationResult($"{nameof(StartTime)} ({StartTime}) must be before {nameof(EndTime)} ({EndTime}).");
        }
    }
}
