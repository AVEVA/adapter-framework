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
namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <summary>
/// Defines batching strategy
/// </summary>
public abstract class BatchingStrategyOptimizer
{
    /// <summary>
    /// Determine if an update to the recommended batch size is needed.
    /// </summary>
    /// <param name="itemCount">The number of items in the serialized collection.</param>
    /// <param name="byteCount">The number of resulting bytes after the collection was serialized.</param>
    /// <param name="over">This update exceeded the batch size limit currently in use.</param>
    public abstract void Update(int itemCount, int byteCount, bool over);
}
