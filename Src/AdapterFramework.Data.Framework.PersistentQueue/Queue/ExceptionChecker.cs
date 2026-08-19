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
using System.IO;

namespace AdapterFramework.Data.Framework.PersistentQueue.Queue;

internal static class ExceptionChecker
{
    private const string SpaceKeyword = "SPACE";

    public static bool IsOutOfDiskSpaceException(IOException ex)
    {
        // When we call stream.Write() (or stream.SetLength on Windows, but not on Ubuntu)
        //  and run out of disk space, testing has shown that Windows will throw an IOException
        //  with message "There is not enough space on the disk : 'C:\path\file'"
        //  and an HResult of -2147024784
        // Meanwhile on Ubuntu, an IOException is thrown with the message "No space left on device"
        //  and an HResult of 28.
        // This leads to a concern that other OS flavors would return different messages and HResults.
        // But one common thing appears to be the word "space" in the exception...
        // So the platform agnostic way to catch if the IOException is an out of disk space exception, is to
        // check whether it has the word "space" in it.
        return ex.Message.ToUpperInvariant().Contains(SpaceKeyword, StringComparison.InvariantCulture);
    }
}
