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
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.DataFlow.Strategy;

/// <summary>
/// The <see cref="BatchSizeOptimizer"/> attempts to determine the optimum
/// batch size based on the serialized data size, based on a maximum byte
/// count limit (e.g. 192KB limit for sending data to OMF endpoint).
///
/// Note: This class is not thread-safe (intentionally so). As an example,
/// see how it is used in the <see cref="SerializationBlock"/> with feedback
/// to the <see cref="DataGroupingBlock"/> or <see cref="TypesStreamsGroupingBlock"/>.
/// </summary>
public class BatchSizeOptimizer : BatchingStrategyOptimizer
{
    #region Private Fields

    // These are all relative to the byte count, not the item count
    private readonly int _upperBound;
    private readonly int _lowerBound;
    private readonly int _midpoint;
    private readonly int _lowerMid;
    private readonly int _upperMid;
    private readonly int _deltaLimit;
    private readonly Action<int> _onChange;

    private bool _previouslyOver;
    private int _previousEstimate;
    private int _previousByteCount;
    private Action<int, int, bool> _update;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchSizeOptimizer"/> class.
    /// </summary>
    /// <param name="tuningParameters">Tuning parameters for batching strategy.</param>
    /// <param name="maxByteCount">The maximum byte count (e.g. 192 KB for OCS).</param>
    /// <param name="onChange">A callback to inform the client of a change in the recommended batch size.</param>
    public BatchSizeOptimizer(TuningParameters tuningParameters, int maxByteCount, Action<int> onChange)
    {
        ThrowHelper.ThrowIfArgumentNull(tuningParameters, nameof(tuningParameters));
        ThrowHelper.ThrowIfArgumentNull(onChange, nameof(onChange));

        _upperBound = (int)(tuningParameters.UpperBoundFactor * maxByteCount);

        _midpoint = (int)(tuningParameters.MidpointFactor * maxByteCount);

        _lowerBound = (int)(tuningParameters.LowerBoundFactor * maxByteCount);

        _lowerMid = (_lowerBound + _midpoint) >> 1;

        _upperMid = (_upperBound + _midpoint) >> 1;

        _deltaLimit = (int)(tuningParameters.DeltaLimitFactor * maxByteCount);

        _previousEstimate = tuningParameters.InitialUpperLimit;

        _previousByteCount = maxByteCount;

        _onChange = onChange;

        _update = UpdateInitial;
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public override void Update(int itemCount, int byteCount, bool over)
    {
        _update(itemCount, byteCount, over);
    }

    #endregion

    #region Private Methods

    private static int GetEstimate(int itemCount, double efficiency)
    {
        var newEstimate = (int)(itemCount / efficiency);

        return newEstimate;
    }

    private static double CalculateEfficiency(int byteCount, int maxByteCount)
    {
        return (double)byteCount / maxByteCount;
    }

    private void UpdateInitial(int itemCount, int byteCount, bool over)
    {
        _update = UpdateNormal;

        UpdateEstimate(itemCount, byteCount, over);
    }

    private void UpdateNormal(int itemCount, int byteCount, bool over)
    {
        if (itemCount != _previousEstimate)
        {
            return;
        }

        UpdateEstimate(itemCount, byteCount, over);
    }

    private void UpdateEstimate(int itemCount, int byteCount, bool over)
    {
        if (!over && Math.Abs(_previousByteCount - byteCount) < _deltaLimit)
        {
            return;
        }

        int target;

        if (over)
        {
            target = _previouslyOver ? _lowerBound : _lowerMid;
        }
        else if (byteCount > _upperBound)
        {
            target = _previouslyOver ? _lowerBound : _lowerMid;
        }
        else if (byteCount > _upperMid)
        {
            target = _previouslyOver ? _lowerBound : _lowerMid;
        }
        else if (byteCount < _lowerBound)
        {
            target = _previouslyOver ? _lowerBound : _lowerMid;
        }
        else if (byteCount < _lowerMid)
        {
            target = _previouslyOver ? _lowerMid : _midpoint;
        }
        else
        {
            target = _previouslyOver ? _lowerMid : _midpoint;
        }

        var efficiency = CalculateEfficiency(byteCount, target);

        var newEstimate = GetEstimate(itemCount, efficiency);

        if (newEstimate == _previousEstimate)
        {
            return;
        }

        _onChange(newEstimate);

        _previousEstimate = newEstimate;

        _previousByteCount = byteCount;

        _previouslyOver = over;
    }

    #endregion
}
