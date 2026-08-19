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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Extensions.Tests")]
[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Abstractions.Tests")]
namespace AdapterFramework.Data.Framework.Extensions;

public static class StringExtensions
{
    #region Internal Constants

    /// <summary>
    /// SDS currently defines 100 characters as the maximum length for Type and Stream IDs
    /// </summary>
    internal const int MaxSdsIdentifierLength = 100;

    #endregion

    #region Internal Fields

    internal static readonly IReadOnlyDictionary<char, string> EdsSubstitutionCharactersMap = new Dictionary<char, string>
    {
        { '>', "%3e" },
        { '<', "%3c" },
        { '/', "%2f" },
        { ':', "%3a" },
        { '?', "%3f" },
        { '#', "%23" },
        { '[', "%5b" },
        { ']', "%5d" },
        { '@', "%40" },
        { '!', "%21" },
        { '$', "%24" },
        { '&', "%26" },
        { '*', "%2a" },
        { '\'', "%27" },
        { '"', "%22" },
        { '(', "%28" },
        { ')', "%29" },
        { '\\', "%5c" },
        { '+', "%2b" },
        { ',', "%2c" },
        { ';', "%3b" },
        { '=', "%3d" },
        { '|', "%7c" },
        { '`', "%60" },
        { '{', "%7b" },
        { '}', "%7d" },
    };

    internal static readonly IReadOnlyDictionary<char, string> OmfSubstitutionCharactersMap = new Dictionary<char, string>
    {
        { '*', "%2a" },
        { '\'', "%27" },
        { '?', "%3f" },
        { ';', "%3b" },
        { '{', "%7b" },
        { '}', "%7d" },
        { '[', "%5b" },
        { ']', "%5d" },
        { '|', "%7c" },
        { '`', "%60" },
        { '"', "%22" },
        { '\\', "%5c" },
    };

    #endregion

    #region Private Constants

    private const string AssertMessageTemplate = "Length of substitution strings in {0} collection must match the {1} value. Not matching key: {2} value: {3}.";
    private const int SubstitutionSubStringLength = 3;
    private const int SubstitutionMoveLength = SubstitutionSubStringLength - 1;
    private const char StartCharacter = '%';

    #endregion

    #region Private Fields

    private static readonly IReadOnlyDictionary<string, char> _reversedEdsSubstitutionCharactersMap = EdsSubstitutionCharactersMap.ToDictionary(x => x.Value, x => x.Key);

    #endregion

    #region Static Constructor

    static StringExtensions()
    {
        foreach (var (character, substitution) in EdsSubstitutionCharactersMap)
        {
            Debug.Assert(substitution.Length == SubstitutionSubStringLength,
                string.Format(CultureInfo.InvariantCulture, AssertMessageTemplate, nameof(EdsSubstitutionCharactersMap), nameof(SubstitutionSubStringLength), character, substitution));
        }

        foreach (var (character, substitution) in OmfSubstitutionCharactersMap)
        {
            Debug.Assert(substitution.Length == SubstitutionSubStringLength,
                string.Format(CultureInfo.InvariantCulture, AssertMessageTemplate, nameof(OmfSubstitutionCharactersMap), nameof(SubstitutionSubStringLength), character, substitution));
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Encodes an identifier string for storing on a file system, by replacing unsupported OMF and file system characters with %HEX code of the given character
    /// and enforces maximum identifier length to <see cref="MaxSdsIdentifierLength"/> by trimming it to the maximum size.
    /// </summary>
    /// <param name="identifier">Identifier string to encode.</param>
    /// <returns>Identifier string with encoded OMF and file system unsupported characters.</returns>
    /// <remarks>This method throws an exception when <paramref name="identifier"/> is null or empty.</remarks>
    public static string ToEdsIdentifier(this string identifier)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(identifier, nameof(identifier));

        return PrefixReplaceCharactersEnforceMaxSize(identifier, null, EdsSubstitutionCharactersMap, MaxSdsIdentifierLength);
    }

    /// <summary>
    /// Encodes an identifier string for sending to an OMF destination, by replacing OMF unsupported characters with %HEX code of the given character
    /// and enforces maximum identifier length to <see cref="MaxSdsIdentifierLength"/> by trimming it to the maximum size.
    /// </summary>
    /// <param name="identifier">Identifier string to encode.</param>
    /// <returns>Identifier string with encoded OMF unsupported characters.</returns>
    /// <remarks>This method throws an exception when <paramref name="identifier"/> is null or empty.</remarks>
    public static string ToOmfIdentifier(this string identifier)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(identifier, nameof(identifier));

        return PrefixReplaceCharactersEnforceMaxSize(identifier, null, OmfSubstitutionCharactersMap);
    }

    /// <summary>
    /// Applies <see paramref="prefix"/> to <paramref name="identifier"/> and encodes the <see paramref="identifier"/> string for sending to an OMF destination, by replacing OMF unsupported characters with %HEX code of the given character
    /// and enforces maximum identifier length to <see cref="MaxSdsIdentifierLength"/> by trimming it to the maximum size.
    /// </summary>
    /// <param name="identifier">String identifier to encode.</param>
    /// <param name="prefix">String to be used as the <paramref name="identifier"/> prefix.</param>
    /// <returns>Prefixed identifier string with encoded OMF unsupported characters.</returns>
    /// <remarks>
    /// This method throws an exception when <paramref name="identifier"/> is null or empty.
    /// For performance reasons <paramref name="prefix"/> string is expected to be sanitized - not containing OMF unsupported characters.
    /// Call <see cref="ToOmfIdentifier"/> on the <paramref name="prefix"/> string before using it in this method.
    /// </remarks>
    public static string ToPrefixedOmfIdentifier(this string identifier, string prefix)
    {
        ThrowHelper.ThrowIfArgumentNullEmptyOrWhiteSpace(identifier, nameof(identifier));

        return PrefixReplaceCharactersEnforceMaxSize(identifier, prefix, OmfSubstitutionCharactersMap);
    }

    /// <summary>
    /// Converts a string that has been encoded by <see cref="ToOmfIdentifier"/>, <see cref="ToEdsIdentifier"/>
    /// or <see cref="ToPrefixedOmfIdentifier"/> extension methods to its original form.
    /// </summary>
    /// <param name="identifier">Encoded identifier string.</param>
    public static string DecodeIdentifier(this string identifier)
    {
        return ReconstructOriginalId(identifier);
    }

    /// <summary>
    /// Returns true if the string contains any unsupported file name characters.
    /// </summary>
    /// <param name="value">Sting value to check for file name unsupported characters.</param>
    /// <param name="unsupportedCharacters">Outputs a list of all unsupported characters founds in the <paramref name="value"/>.</param>
    /// <returns>A boolean - true if there are unsupported characters present; otherwise false.</returns>
    public static bool ContainsInvalidFileNameCharacters(this string value, out List<char> unsupportedCharacters)
    {
        ThrowHelper.ThrowIfArgumentNull(value, nameof(value));

        unsupportedCharacters = new List<char>();

        var invalidFileCharactersSet = Path.GetInvalidFileNameChars().ToHashSet();

        foreach (var character in value)
        {
            if (invalidFileCharactersSet.Contains(character))
            {
                unsupportedCharacters.Add(character);
            }
        }

        return unsupportedCharacters.Count != 0;
    }

    /// <summary>
    /// Replaces characters listed in the <paramref name="substitutionMap"/> map will be replaced with the given string substitutions. 
    /// </summary>
    /// <param name="value">String value to replace characters in.</param>
    /// <param name="substitutionMap">Dictionary with characters to replace and string replacement values.</param>
    /// <returns><paramref name="value"/> with replaced characters that were found in <paramref name="substitutionMap"/>.</returns>
    public static string ReplaceCharacters(this string value, IReadOnlyDictionary<char, string> substitutionMap)
    {
        ThrowHelper.ThrowIfArgumentNull(value, nameof(value));
        ThrowHelper.ThrowIfArgumentNull(substitutionMap, nameof(substitutionMap));

        return PrefixReplaceCharactersEnforceMaxSize(value, null, substitutionMap);
    }

    /// <summary>
    ///  Returns true if the string contains any unsupported OMF or file system characters.
    /// </summary>
    /// <param name="value">The string to check for unsupported characters.</param>
    /// <param name="unsupportedCharacters">Outputs a list of all unsupported characters founds in the <paramref name="value"/>.</param>
    /// <returns>A boolean - true if there are unsupported characters present; otherwise false.</returns>
    public static bool ContainsEdsIdentifierUnsupportedCharacters(this string value, out List<char> unsupportedCharacters)
    {
        unsupportedCharacters = new List<char>();

        if (value.IsNullOrEmpty())
        {
            return false;
        }

        foreach (var character in value)
        {
            if (EdsSubstitutionCharactersMap.ContainsKey(character))
            {
                unsupportedCharacters.Add(character);
            }
        }

        return unsupportedCharacters.Count != 0;
    }

    /// <summary>
    /// Returns true if the string contains leading or trailing spaces.
    /// </summary>
    /// <param name="value">The string to check for leading or trailing spaces.</param>
    /// <returns>A boolean - true if the string contains leading or trailing spaces; otherwise false.</returns>
    public static bool ContainsLeadingOrTrailingSpaces(this string value)
    {
        if (value.IsNullOrEmpty())
        {
            return false;
        }

        return !value.Equals(value.Trim(), System.StringComparison.InvariantCultureIgnoreCase);
    }

    #endregion

    #region Private Methods

    private static string PrefixReplaceCharactersEnforceMaxSize(string value, string prefix, IReadOnlyDictionary<char, string> substitutionMap, int maximumLength = int.MaxValue)
    {
        var encodedIdentifier = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            encodedIdentifier.Append(prefix);
        }

        foreach (var character in value)
        {
            if (substitutionMap.TryGetValue(character, out var replacement))
            {
                if (SizeLimitReached(encodedIdentifier.Length + SubstitutionSubStringLength, maximumLength))
                {
                    break;
                }

                encodedIdentifier.Append(replacement);
            }
            else
            {
                if (SizeLimitReached(encodedIdentifier.Length, maximumLength))
                {
                    break;
                }

                encodedIdentifier.Append(character);
            }
        }

        return encodedIdentifier.ToString();
    }

    private static string ReconstructOriginalId(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return identifier;
        }

        var decodedIdentifier = new StringBuilder();
        for (var i = 0; i < identifier.Length; i++)
        {
            if (identifier[i].Equals(StartCharacter))
            {
                if (identifier.Length > i + SubstitutionMoveLength)
                {
                    var substring = identifier.Substring(i, SubstitutionSubStringLength);
                    if (_reversedEdsSubstitutionCharactersMap.TryGetValue(substring, out var originalValue))
                    {
                        decodedIdentifier.Append(originalValue);
                        i += SubstitutionMoveLength;
                        continue;
                    }
                }
            }

            decodedIdentifier.Append(identifier[i]);
        }

        return decodedIdentifier.ToString();
    }

    private static bool SizeLimitReached(int lengthAfterAppend, int maximumLength) => lengthAfterAppend > maximumLength;

    #endregion
}
