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
using System.IO;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;

namespace AdapterFramework.Data.Framework.PersistentQueue.Interfaces;

internal interface ISerializer
{
    void SerializeQueueState(Stream stream, QueueState queueState);
    void SerializeReaderState(Stream stream, QueueState queueState);
    void SerializeWriterState(Stream stream, QueueState queueState);
    QueueState DeserializeQueueState(Stream stream);
    void SerializeDataItem(Stream stream, DataItem item);

    DataItem DeserializeDataItem(Stream stream);

    bool TryDeserializeNextValidDataItem(Stream stream, out DataItem dataItem);
}
