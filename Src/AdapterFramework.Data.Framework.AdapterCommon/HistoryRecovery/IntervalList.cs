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
using System.Linq;
using AdapterFramework.Data.Framework.Abstractions.HistoryRecovery;

namespace AdapterFramework.Data.Framework.AdapterCommon.HistoryRecovery;

public class IntervalList : IList<Interval>
{
    private readonly List<Interval> _intervals;
    private readonly object _lock = new object();

    public IntervalList()
    {
        _intervals = new List<Interval>();
    }

    public bool IsReadOnly
    {
        get { return false; }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _intervals.Count;
            }
        }
    }

    public Interval this[int index]
    {
        get
        {
            lock (_lock)
            {
                return _intervals[index];
            }
        }
        set
        {
            lock (_lock)
            {
                _intervals[index] = value;
            }
        }
    }

    public IEnumerator<Interval> GetEnumerator()
    {
        return Clone().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return Clone().GetEnumerator();
    }

    public void Add(Interval item)
    {
        lock (_lock)
        {
            _intervals.Add(item);
        }
    }

    public bool Remove(Interval item)
    {
        lock (_lock)
        {
            return _intervals.Remove(item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _intervals.Clear();
        }
    }

    public bool Contains(Interval item)
    {
        lock (_lock)
        {
            return _intervals.Contains(item);
        }
    }

    public bool Contains(DateTime? startTime)
    {
        lock (_lock)
        {
            return _intervals.All(i => i.StartTime != startTime);
        }
    }

    public void CopyTo(Interval[] array, int arrayIndex)
    {
        lock (_lock)
        {
            _intervals.CopyTo(array, arrayIndex);
        }
    }

    public int IndexOf(Interval item)
    {
        lock (_lock)
        {
            return _intervals.IndexOf(item);
        }
    }

    public void Insert(int index, Interval item)
    {
        lock (_lock)
        {
            _intervals.Insert(index, item);
        }
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            _intervals.RemoveAt(index);
        }
    }

    public Interval GetFirstInterval()
    {
        lock (_lock)
        {
            return _intervals.Count > 0 ? _intervals[0] : default;
        }
    }

    public Interval GetLastInterval()
    {
        lock (_lock)
        {
            return _intervals.Count > 0 ? _intervals[^1] : default;
        }
    }

    public void AddOrUpdate(Interval intervalToUpdate, DateTime? newEndTime = null, DateTime? newLastReadTime = null, DateTime? newStartTime = null)
    {
        if (intervalToUpdate == null)
        {
            return;
        }

        lock (_lock)
        {
            if (_intervals.Contains(intervalToUpdate))
            {
                var index = _intervals.IndexOf(intervalToUpdate);
                _intervals[index].StartTime = newStartTime ?? intervalToUpdate.StartTime;
                _intervals[index].EndTime = newEndTime ?? intervalToUpdate.EndTime;
                _intervals[index].LastReadTime = newLastReadTime ?? intervalToUpdate.LastReadTime;
            }
            else
            {
                _intervals.Add(intervalToUpdate);
            }
        }
    }

    public Interval FindIntervalByStartTime(DateTime? startTime)
    {
        lock (_lock)
        {
            return _intervals.FirstOrDefault(i => i.StartTime == startTime);
        }
    }

    private List<Interval> Clone()
    {
        lock (_lock)
        {
            return new List<Interval>(_intervals);
        }
    }
}
