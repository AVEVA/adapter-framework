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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Configuration.Attributes;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

/// <summary>
/// This class is meant to represent state of a discovery operation.
/// </summary>
public class DiscoveryState : EdgeConfigurationBase
{
    private const string UnsupportedCharactersMessage = "The supplied discovery id {0} contains the following unsupported characters: {1} .";

    [Id]
    public string Id { get; set; }

    public string Query { get; set; }

    [ReadOnly(true)]
    public DateTime? StartTime { get; set; }

    [ReadOnly(true)]
    public DateTime? EndTime { get; set; }

    [ReadOnly(true)]
    public int Progress { get; set; }

    [ReadOnly(true)]
    public int ItemsFound { get; set; }

    [ReadOnly(true)]
    public int NewItems { get; set; }

    [ReadOnly(true)]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Discovery result URI is persisted as text in configuration payloads.")]
    public string ResultUri { get; set; }

    public bool AutoSelect { get; set; }

    [ReadOnly(true)]
    public OperationStatus Status { get; set; }

    [ReadOnly(true)]
    public string Errors { get; set; }

    public override IEnumerable<ValidationResult> Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString();
        }

        if (Id.ContainsInvalidFileNameCharacters(out var unsupportedCharacters))
        {
            yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, UnsupportedCharactersMessage, Id, string.Join(" ", unsupportedCharacters)));
        }
    }
}
