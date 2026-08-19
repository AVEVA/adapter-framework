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
using System.Runtime.InteropServices;
using AdapterFramework.Data.Framework.Common.Utilities;
using Xunit;

namespace AdapterFramework.Data.Framework.Common.Tests.Utilities;

public class ShellCommandExecutor_Tests : IDisposable
{
    private const string AdapterFrameworkDirectoryName = "AdapterFramework";
    private const string UnitTestDirectoryName = "BashUnitTests";
    private static readonly bool _runsOnWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static readonly string _writableLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), AdapterFrameworkDirectoryName, UnitTestDirectoryName);
    private bool _disposed;

    public ShellCommandExecutor_Tests()
    {
        if (!Directory.Exists(_writableLocation))
        {
            Directory.CreateDirectory(_writableLocation);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ShellCommandExecutor_BashRun_InvalidInput_Test(string args)
    {
        var exceptionThrown = false;
        try
        {
            ShellCommandExecutor.BashRun(args, out _);
        }
        catch (Exception)
        {
            exceptionThrown = true;
        }

        Assert.True(exceptionThrown);
    }

    [Fact]
    public void ShellCommandExecutor_BashRun_Test()
    {
        if (!_runsOnWindows)
        {
            var fileLocation = Path.Combine(_writableLocation, "testFile");
            Assert.False(File.Exists(fileLocation));

            var result = ShellCommandExecutor.BashRun($"touch {fileLocation}", out var output);

            Assert.True(result);
            Assert.Empty(output);
            Assert.True(File.Exists(fileLocation));
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        if (Directory.Exists(_writableLocation))
        {
            Directory.Delete(_writableLocation, true);
        }

        _disposed = true;
    }
}
