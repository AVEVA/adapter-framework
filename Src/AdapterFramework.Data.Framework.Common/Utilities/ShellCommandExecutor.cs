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
using System.Diagnostics;
using System.IO;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Common.Utilities;

public static class ShellCommandExecutor
{
    /// <summary>
    /// Runs desired bash command.
    /// </summary>
    /// <param name="cmd">Command to execute.</param>
    /// <param name="output">Standard output from execution of the command.</param>
    /// <returns>A boolean indicating if the bash commands run successfully.</returns>
    public static bool BashRun(string cmd, out string output)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(cmd, nameof(cmd));

        output = string.Empty;

        var shell = GetLinuxShell();

        if (shell == null)
        {
            return false;
        }

        Process process = null;
        try
        {
            var escapedArgs = cmd.Replace("\"", "\\\"", StringComparison.InvariantCulture);

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = shell,
                    Arguments = $"-c \"{escapedArgs}\"",
                },
            };

            process.Start();

            output = process.StandardOutput.ReadToEnd();

            process.WaitForExit();

            return true;
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// Returns available Linux shell or NULL when none is available.
    /// </summary>
    /// <returns>Path to available Linux shell or NULL.</returns>
    private static string GetLinuxShell()
    {
        var linuxShells = new List<string> { "/bin/bash", "/bin/sh", "/bin/ash", "/bin/dash" };

        foreach (var shell in linuxShells)
        {
            if (File.Exists(shell))
            {
                return shell;
            }
        }

        return null;
    }
}
