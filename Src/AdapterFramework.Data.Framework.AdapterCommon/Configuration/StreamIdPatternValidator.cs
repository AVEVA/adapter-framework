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
using System.Linq;
using System.Text.RegularExpressions;

namespace AdapterFramework.Data.Framework.AdapterCommon.Configuration;

public static class StreamIdPatternValidator
{
    private const string StreamIdPatternRegex = @"(?={)|(?<=})";

    public static bool IsValidDefaultStreamIdPattern(string patternToValidate, string defaultStreamIdPattern, string[] supportedParameters, out string error)
    {
        error = null;
        var validParametersWithExample = $"Supported substitution parameters: {string.Join(", ", supportedParameters)}. Example: {defaultStreamIdPattern}";

        if (string.IsNullOrWhiteSpace(patternToValidate))
        {
            error = $"Cannot be null, empty or whitespace. {validParametersWithExample}.";
            return false;
        }

        var patternSplit = Regex.Split(patternToValidate, StreamIdPatternRegex);
        var unsupportedKeywords = new List<string>();
        var parameterSpecified = false;
        foreach (var parameter in patternSplit)
        {
            if (parameter.StartsWith('{') && parameter.EndsWith('}'))
            {
                if (!supportedParameters.Contains(parameter[1..^1], StringComparer.OrdinalIgnoreCase))
                {
                    unsupportedKeywords.Add(parameter);
                    continue;
                }

                parameterSpecified = true;
            }
        }

        if (unsupportedKeywords.Count > 0)
        {
            error = $"Unsupported substitution parameters specified: {string.Join(", ", unsupportedKeywords)}. {validParametersWithExample}.";
            return false;
        }

        if (!parameterSpecified)
        {
            error = $"At least one substitution parameter must be specified. {validParametersWithExample}.";
            return false;
        }

        return true;
    }
}
