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
namespace AdapterFramework.Data.Framework.Abstractions.Services;

public interface IDefaultStreamIdGenerator
{
    /// <summary>
    /// Set the default adapter-generated stream ID pattern by providing the pattern and keywords. 
    /// </summary>
    /// <param name="adapterGeneratedStreamIdPattern">The pattern to generate the default stream ID.</param>
    /// <param name="keywords">The keywords used in the pattern to generate the default stream ID.</param>
    void SetDefaultStreamIdPattern(string adapterGeneratedStreamIdPattern, params string[] keywords);

    /// <summary>
    /// Returns a stream ID to be used based on the given values. This should be used if no Stream ID was defined for this stream in the
    /// data selection file. If a stream ID exists in the data selection file, use the one from the file instead.
    /// </summary>
    /// <param name="values">The values to replace the keywords. They should be in the same order as the keywords were defined.
    /// Null values will completely remove all occurrences of its keyword with no replacement.</param>
    /// <returns>The stream with the substituted values.</returns>
    string GetDefaultStreamId(params string[] values);
}
