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
using AdapterFramework.Data.Framework.Abstractions.Scheduling;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.Scheduling;

public class PeriodicSchedule : ISchedule
{
    #region Private Fields

    private readonly TimeSpan _period;
    private readonly DateTime _referenceTime;
    private DateTime _nextRunTime;

    #endregion

    #region Public Constructor

    public PeriodicSchedule(string id, TimeSpan period, TimeSpan? offset)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(id, nameof(id));

        if (period.Ticks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(period), $"The {nameof(period)} can not have a negative or zero length timespan.");
        }

        Id = id;
        _period = period;

        if (offset != null)
        {
            _referenceTime = DateTime.Today.Add(offset.Value).ToUniversalTime();
        }
        else
        {
            _referenceTime = DateTime.UtcNow;
        }
    }

    #endregion

    #region Public Properties

    public string Id { get; }

    #endregion

    #region Public Methods

    public DateTime GetNextRunTime(DateTime time) => _nextRunTime <= time ? CalculateNextRunTime(time) : _nextRunTime;

    #endregion

    #region Private Methods

    private DateTime CalculateNextRunTime(DateTime time)
    {
        var nextRunTime = _referenceTime > _nextRunTime ? _referenceTime : _nextRunTime;

        if (nextRunTime <= time)
        {
            var timeSpan = time.Subtract(nextRunTime);
            var addedTime = _period.Ticks - (timeSpan.Ticks % _period.Ticks);
            nextRunTime = time.AddTicks(addedTime);
        }

        _nextRunTime = nextRunTime;
        return _nextRunTime;
    }

    #endregion
}
