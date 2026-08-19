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
using System.IO;
using System.Threading;
using AdapterFramework.Data.Framework.PersistentQueue.Queue;
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

internal static class TestHelper
{
    public static void DeleteDirectoryWithRetry(string dir)
    {
        if (Directory.Exists(dir))
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    public static void AssertEqual(IList<DataItem> dataItemList1, IList<DataItem> dataItemList2)
    {
        if (dataItemList1.Count != dataItemList2.Count)
        {
            throw new Exception($"Data item list counts not equal. l1: {dataItemList1.Count}. l2: {dataItemList2.Count}.");
        }

        for (int i = 0; i < dataItemList1.Count; i++)
        {
            try
            {
                AssertEqual(dataItemList1[i], dataItemList2[i]);
            }
            catch (Exception ex)
            {
                Assert.Throws<Exception>(() => $"Data item {i} in the list not equal. Exception message: {ex.Message}");
            }
        }
    }

    public static void AssertEqual(DataItem dataItem1, DataItem dataItem2)
    {
        if (dataItem1.Version != dataItem2.Version)
        {
            throw new Exception("Data item versions not equal.");
        }

        if (dataItem1.Data.Length != dataItem2.Data.Length)
        {
            throw new Exception("Data item data lengths not equal.");
        }

        for (int i = 0; i < dataItem1.Data.Length; i++)
        {
            if (dataItem1.Data[i] != dataItem2.Data[i])
            {
                Assert.Throws<Exception>(() => $"{dataItem1.Data[i]} != {dataItem2.Data[i]}");
            }
        }
    }

    public static List<DataItem> GenerateRandomDataItems(int numMessages, int msgByteSize)
    {
        var msgList = new List<DataItem>();

        Random rnd = new Random();
        for (int i = 0; i < numMessages; i++)
        {
            var item = new byte[msgByteSize];
            rnd.NextBytes(item);

            msgList.Add(new DataItem(DataItemVersion.V1, item));
        }

        return msgList;
    }

    public static void ThrowOutOfDiskException()
    {
        throw new IOException(@"No space left on device", 28); // Ubuntu Out of Disk exception
    }
}
