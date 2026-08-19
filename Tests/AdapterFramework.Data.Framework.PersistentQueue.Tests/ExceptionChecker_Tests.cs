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
using Xunit;

namespace AdapterFramework.Data.Framework.PersistentQueue.Tests;

public class ExceptionChecker_Tests
{
    [Fact]
    public void ExceptionChecker_IsOutOfDiskSpaceException_CorrectlyIdentifiesExceptions()
    {
        // Attempt to reproduce the Windows exception..
        var windowsException = new IOException(@"There is not enough space on the disk : 'C:\path\file'", -2147024784);
        Assert.True(ExceptionChecker.IsOutOfDiskSpaceException(windowsException));

        // Attempt to reproduce the Ubuntu exception..
        var ubuntuException = new IOException(@"No space left on device", 28);
        Assert.True(ExceptionChecker.IsOutOfDiskSpaceException(ubuntuException));

        // Ensure a different IOException is not caught...
        var eosException = new EndOfStreamException("Unable to read beyond the end of the stream.");
        Assert.False(ExceptionChecker.IsOutOfDiskSpaceException(eosException));
    }
}
