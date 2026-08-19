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
namespace AdapterFramework.Data.Framework.DataFlow.Strategy;

/// <summary>
/// Provides tuning parameters for message batching algorithm.
/// </summary>
public class TuningParameters
{
    /// <summary>
    /// Used to calculate lower bound for the batch size in <see cref="BatchSizeOptimizer"/>.
    /// </summary>
    public double LowerBoundFactor { get; set; } = 0.988;

    /// <summary>
    /// Used to calculate upper bound for the batch size in <see cref="BatchSizeOptimizer"/>.
    /// </summary>
    public double UpperBoundFactor { get; set; } = 0.998;

    /// <summary>
    /// Used to set a Mid point in the <see cref="BatchSizeOptimizer"/> to make sure we're using batch size effectively.
    /// </summary>
    public double MidpointFactor => (UpperBoundFactor + LowerBoundFactor) / 2;

    /// <summary>
    /// Used to calculate delta limit in <see cref="BatchSizeOptimizer"/> to figure out if batch size adjustment is necessary.
    /// </summary>
    public double DeltaLimitFactor { get; set; } = 0.010;

    /// <summary>
    /// Used as an initial previous estimate in the <see cref="BatchSizeOptimizer"/>.
    /// </summary>
    public int InitialUpperLimit { get; set; } = 10_000;
}
