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
using System.Threading.Tasks;
using AdapterFramework.Data.Framework.Host;

namespace AdapterFramework.Data.System.Host;

internal sealed class Program
{
    /// <summary>
    /// Note: DO NOT modify the content of this file.
    /// The entry point to start the system host.
    /// </summary>
    /// <param name="args">The arguments to start the adapter host. </param>
    /// <returns>The running task of the adapter host. </returns>
    private static async Task Main(string[] args)
    {
        var enforceBetaTimeout = false;

// -:cnd:noEmit
#if BETATIMEOUT
        enforceBetaTimeout = true;
#endif

// -:cnd:noEmit

        // Start the system host. 
        await SystemHost.RunAsync(args, enforceBetaTimeout);
    }
}
