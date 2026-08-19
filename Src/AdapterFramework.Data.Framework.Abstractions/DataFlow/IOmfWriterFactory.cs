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
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;

namespace AdapterFramework.Data.Framework.Abstractions.DataFlow;

/// <summary>
/// A factory abstraction for a component that can create <see cref="IOmfWriter"/> instances with custom
/// <see cref="IOmfWriterConfiguration"/> and <see cref="OmfWriterType"/>.
/// </summary>
public interface IOmfWriterFactory
{
    /// <summary>
    /// Creates and configures <see cref="IOmfWriter"/> instance using the <see paramref="writerConfiguration"/> of
    /// specified <see paramref="writerType"/>.
    /// </summary>
    /// <param name="writerConfiguration">Configuration for the <see cref="IOmfWriter"/> instance.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="writerType">Type of the <see cref="IOmfWriter"/> instance.</param>
    /// <param name="bufferingConfiguration">Type of the <see cref="IBufferingConfiguration"/> instance.</param>
    /// <param name="deviceStatusHandler">Action to be called when the writer status changes (i.e. error or good state)</param>
    /// <returns>An instance of an object with an IOmfWriter interface.</returns>
    IOmfWriter GetOmfWriterInstance(IEndpointConfiguration writerConfiguration, ILogger logger, OmfWriterType writerType, IBufferingConfiguration bufferingConfiguration, Action<bool> deviceStatusHandler);
}
