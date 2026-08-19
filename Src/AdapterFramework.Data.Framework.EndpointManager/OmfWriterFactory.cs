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
using System.IO;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.EndpointManager;

public class OmfWriterFactory : IOmfWriterFactory
{
    #region Private Constants

    private const string EgressDebugLogsDirectoryName = "EgressDebugLogs";

    #endregion

    #region Private Fields

    private readonly ISerializer _serializer;
    private readonly ICompressor _compressor;
    private readonly IEdgeDataProtector _dataProtector;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IApplicationManifest _applicationManifest;
    private readonly IEdgeEventProvider _edgeEventProvider;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="OmfWriterFactory"/> class.
    /// </summary>
    /// <param name="dataProtector"><see cref="IEdgeDataProtector"/> instance.</param>
    /// <param name="serializer"><see cref="ISerializer"/> instance.</param>
    /// <param name="configurationProvider"><see cref="IConfigurationProvider"/> instance.</param>
    /// <param name="applicationManifest"><see cref="IApplicationManifest"/> instance.</param>
    /// <param name="compressor"><see cref="ICompressor"/> instance.</param>
    /// <param name="edgeEventProvider"><see cref="IEdgeEventProvider"/> instance.</param>
    public OmfWriterFactory(
        IEdgeDataProtector dataProtector,
        ISerializer serializer,
        IConfigurationProvider configurationProvider,
        IApplicationManifest applicationManifest,
        ICompressor compressor = null,
        IEdgeEventProvider edgeEventProvider = null)
    {
        _serializer = serializer;
        _compressor = compressor;
        _dataProtector = dataProtector;
        _configurationProvider = configurationProvider;
        _applicationManifest = applicationManifest;
        _edgeEventProvider = edgeEventProvider;
    }

    #endregion

    #region Public Methods

    public IOmfWriter GetOmfWriterInstance(
        IEndpointConfiguration writerConfiguration,
        ILogger logger,
        OmfWriterType omfWriterType,
        IBufferingConfiguration bufferingConfiguration,
        Action<bool> deviceStatusHandler)
    {
        ThrowHelper.ThrowIfArgumentNull(writerConfiguration, nameof(writerConfiguration));
        ThrowHelper.ThrowIfArgumentNull(bufferingConfiguration, nameof(bufferingConfiguration));

        var bufferFilesPath = GetBufferPartitionPath(writerConfiguration, omfWriterType, bufferingConfiguration);

        IEdgeEventProvider omfedgeEventProvider = null;
        if (omfWriterType == OmfWriterType.Data)
        {
            omfedgeEventProvider = _edgeEventProvider;
        }

        var debugLogsPath = Path.Combine(
            _configurationProvider.GetCommonApplicationDataDirectoryPath(),
            AdapterFramework.Data.Framework.Abstractions.Constants.EdgeSystemConstants.LoggingDirectoryName,
            EgressDebugLogsDirectoryName,
            omfWriterType.ToString(),
            writerConfiguration.Id);

        return new OmfWriter(
            writerConfiguration,
            logger,
            _serializer,
            _compressor,
            _dataProtector,
            bufferingConfiguration,
            _applicationManifest,
            bufferFilesPath,
            debugLogsPath,
            omfWriterType,
            deviceStatusHandler,
            omfedgeEventProvider);
    }

    #endregion

    #region Private Methods

    private string GetBufferPartitionPath(IEndpointConfiguration writerConfiguration, OmfWriterType omfWriterType, IBufferingConfiguration bufferingConfiguration)
    {
        var partition = string.IsNullOrWhiteSpace(bufferingConfiguration.BufferLocation)
            ? Path.Combine(_configurationProvider.GetCommonApplicationDataDirectoryPath(omfWriterType.ToString()), writerConfiguration.Id)
            : Path.Combine(bufferingConfiguration.BufferLocation, omfWriterType.ToString(), writerConfiguration.Id);

        return partition;
    }

    #endregion
}
