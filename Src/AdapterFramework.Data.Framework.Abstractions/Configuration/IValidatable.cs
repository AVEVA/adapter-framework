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

namespace AdapterFramework.Data.Framework.Abstractions.Configuration;

[Obsolete("IValidatable interface is deprecated, please use IValidatableObject instead.")]
public interface IValidatable
{
    /// <summary>Validates the public properties of a type.</summary>
    /// <param name="errors">A dictionary of validation errors.</param>
    /// <returns>True if valid. False otherwise.</returns>
    /// <remarks>Usually, in the dictionary of validation errors the key is the property name or single word description of the kind of validation,
    /// and the value is the validation error.</remarks>
    [Obsolete("IValidatable interface is deprecated, please use IValidatableObject instead.")]
    bool Validate(out IDictionary<string, string> errors);
}
