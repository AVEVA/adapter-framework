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
namespace AdapterFramework.Data.Framework.Messages;

public class OmfHealthMessage<T> : Message
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OmfHealthMessage{T}"/> class.
    /// </summary>
    /// <param name="count">message count.</param>
    /// <param name="values">an array of values of message of type <typeparamref name="T"/>.</param>
    public OmfHealthMessage(int count, T[] values)
    {
        Count = count;

        Values = values;
    }

    /// <summary>Gets the value count of the OMF health message.</summary>
    /// <value>The value count of the OMF message.</value>
    public int Count { get; }

    /// <summary>Gets the value array of the OMF health message.</summary>
    /// <value>The value array of the OMF message.</value>
    public T[] Values { get; }
}
