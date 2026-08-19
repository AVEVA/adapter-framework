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

namespace AdapterFramework.Data.Framework.Common;

/// <summary>
/// Represents moving average calculator.
///
/// NOTE: This class is not thread-safe (intentionally so for performance reasons).
/// 
/// </summary>
public class MovingAverage
{
    #region Private Fields

    private readonly Queue<long> _movingAverageQueue;
    private readonly int _period;
    private long _sum;
    
    #endregion

    #region Public Constructor

    /// <summary>
    /// Constructor of <see cref="MovingAverage"/> class.
    /// </summary>
    /// <param name="period">Maximum number of samples to hold in include in average result calculation.</param>
    public MovingAverage(int period)
    {
        if (period < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(period), "Must be greater than 0.");
        }

        _sum = 0;
        _period = period;
        _movingAverageQueue = new Queue<long>(period);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds value that represents a sample to the queue to be included in the result.
    /// </summary>
    /// <param name="value">Representation of a sample.</param>
    public void AddSample(long value)
    {
        if (_movingAverageQueue.Count >= _period)
        {
            _sum -= _movingAverageQueue.Dequeue();
        }

        _sum += value;
        _movingAverageQueue.Enqueue(value);
    }

    /// <summary>
    /// Clears all the available samples in the queue.
    /// </summary>
    public void ClearSamples()
    {
        _sum = 0;
        _movingAverageQueue.Clear();
    }

    /// <summary>
    /// Calculates a average from available samples.
    /// </summary>
    /// <returns>Average value of available samples.</returns>
    public double ComputeAverage()
    {
        if (_movingAverageQueue.Count == 0)
        {
            return 0;
        }

        return (double)_sum / _movingAverageQueue.Count;
    }

    #endregion
}
