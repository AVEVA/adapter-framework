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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Scheduling;
using AdapterFramework.Data.Framework.Common.Scheduling.Statistics;

namespace AdapterFramework.Data.Framework.Common.Scheduling;

public sealed class ParallelJob : Job
{
    public ParallelJob(string id, ISchedule schedule, Func<CancellationToken, Task> function, ILogger logger, bool isSuspended) 
        : base(id, schedule, function, new JobStatistics(), logger, isSuspended)
    {
    }
}
