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
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.AdapterCommon.Tests")]

namespace AdapterFramework.Data.Framework.AdapterCommon;

public class DefaultStreamIdGenerator : IDefaultStreamIdGenerator
{
    private readonly object _lock = new object();

    private string _defaultStreamIdPattern;
    private string _adapterGeneratedStreamIdPattern;
    private string[] _patternKeywords;
    private DefaultStreamIdInformation _streamInformation;

    public void SetDefaultStreamIdPattern(string adapterGeneratedStreamIdPattern, params string[] keywords)
    {
        ThrowHelper.ThrowIfArgumentNull(keywords, nameof(keywords));

        lock (_lock)
        {
            if (keywords.Contains(null))
            {
                throw new ArgumentException("Can not have a null value for a key word.", nameof(keywords));
            }

            if (keywords.Contains(string.Empty) || keywords.Any(item => Regex.IsMatch(item, @"\s+")))
            {
                throw new ArgumentException("Can not have empty or white spaces only for a key word.", nameof(keywords));
            }

            if (keywords.Length != keywords.Distinct().Count())
            {
                throw new ArgumentException("Can not have duplicate keywords.", nameof(keywords));
            }

            _adapterGeneratedStreamIdPattern = adapterGeneratedStreamIdPattern;
            _patternKeywords = keywords;
            UpdateDefaultStreamIdInformation();
        }
    }

    /// <inheritdoc />
    public string GetDefaultStreamId(params string[] values)
    {
        ThrowHelper.ThrowIfArgumentNull(values, nameof(values));

        lock (_lock)
        {
            var currentStreamInfo = _streamInformation;

            if (values.Length < currentStreamInfo.NumberOfRequiredParams)
            {
                throw new ArgumentException($"Received only {values.Length} values but needed {currentStreamInfo.NumberOfRequiredParams}", nameof(values));
            }

            return string.Format(CultureInfo.InvariantCulture, currentStreamInfo.FormatPattern, values);
        }
    }

    internal void UpdateDefaultStreamIdPattern(string defaultStreamIdPattern)
    {
        lock (_lock)
        {
            _defaultStreamIdPattern = defaultStreamIdPattern;
            UpdateDefaultStreamIdInformation();
        }
    }

    private void UpdateDefaultStreamIdInformation()
    {
        var streamIdPattern = _defaultStreamIdPattern ?? _adapterGeneratedStreamIdPattern;

        if (streamIdPattern == null)
        {
            throw new ArgumentNullException($"{nameof(_defaultStreamIdPattern)} and {nameof(_adapterGeneratedStreamIdPattern)} can not both be null.");
        }

        if (_patternKeywords == null)
        {
            throw new InvalidOperationException($"Default stream ID pattern keywords must be set. Call {nameof(AdapterCommonService.DefaultStreamIdGenerator.SetDefaultStreamIdPattern)} " +
                                                $"on {nameof(AdapterCommonService.DefaultStreamIdGenerator)} in InitializeAdapterAsync method.");
        }

        var patternIndexToKeywordIndex = new Dictionary<int, int>();

        // Splits on {.} and includes the { } too.
        var streamIdPatternArr = Regex.Split(streamIdPattern, @"(?={)|(?<=})");

        for (int i = 0; i < _patternKeywords.Length; i++)
        {
            var index = i;
            var locations = Enumerable.Range(0, streamIdPatternArr.Length)
            .Where(x => string.Equals(streamIdPatternArr[x], "{" + _patternKeywords[index] + "}", StringComparison.InvariantCultureIgnoreCase));

            foreach (var location in locations)
            {
                patternIndexToKeywordIndex[location] = i;
            }
        }

        int numRequiredParams = 0;
        var streamIdPatternBuilder = new StringBuilder();
        for (var i = 0; i < streamIdPatternArr.Length; i++)
        {
            var replaced = false;
            if (patternIndexToKeywordIndex.ContainsKey(i))
            {
                numRequiredParams = Math.Max(numRequiredParams, patternIndexToKeywordIndex[i] + 1);
                streamIdPatternBuilder.Append(CultureInfo.InvariantCulture, $"{{{patternIndexToKeywordIndex[i]}}}");
                replaced = true;
            }

            if (!replaced)
            {
                if (Regex.IsMatch(streamIdPatternArr[i], @"(?={)|(?<=})") && !Regex.IsMatch(streamIdPatternArr[i], "{\\d+}"))
                {
                    if (streamIdPattern == _defaultStreamIdPattern)
                    {
                        throw new ArgumentException($"Invalid keyword {streamIdPatternArr[i]} found in default stream ID pattern {streamIdPattern}.");
                    }

                    throw new ArgumentException($"Invalid keyword {streamIdPatternArr[i]} found in adapter generated stream ID pattern {streamIdPattern}.");
                }

                streamIdPatternBuilder.Append(streamIdPatternArr[i]);
            }
        }

        _streamInformation = new DefaultStreamIdInformation(string.Join(string.Empty, streamIdPatternBuilder.ToString()), numRequiredParams);
    }

    private struct DefaultStreamIdInformation
    {
        internal DefaultStreamIdInformation(string formatPattern, int numberOfRequiredParams)
        {
            FormatPattern = formatPattern;
            NumberOfRequiredParams = numberOfRequiredParams;
        }

        internal string FormatPattern { get; }
        internal int NumberOfRequiredParams { get; }
    }
}
