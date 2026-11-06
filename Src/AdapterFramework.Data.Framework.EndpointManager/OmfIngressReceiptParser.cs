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
using System.Globalization;
using System.Text.Json;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;

namespace AdapterFramework.Data.Framework.EndpointManager;

/// <summary>
/// Parses the body of a 2xx response from a Platform OMF endpoint into an <see cref="OmfIngressReceipt"/>.
/// </summary>
internal static class OmfIngressReceiptParser
{
    /// <summary>
    /// Parses a response body. Property names match case-insensitively, and values that can't be parsed are left null.
    /// </summary>
    /// <param name="content">The response body.</param>
    /// <returns>The receipt, or null when the body isn't a JSON object with any receipt property.</returns>
    public static OmfIngressReceipt Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            Guid? operationId = null;
            int? errorCount = null;
            TimeSpan? queueAge = null;
            var found = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "Operation-Id", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    operationId = property.Value.ValueKind == JsonValueKind.String && Guid.TryParse(property.Value.GetString(), out var id) ? id : null;
                }
                else if (string.Equals(property.Name, "ErrorCount", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    errorCount = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count) ? count : null;
                }
                else if (string.Equals(property.Name, "QueueAge", StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    queueAge = property.Value.ValueKind == JsonValueKind.String
                        && TimeSpan.TryParse(property.Value.GetString(), CultureInfo.InvariantCulture, out var age) ? age : null;
                }
            }

            return found ? new OmfIngressReceipt(operationId, errorCount, queueAge) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
