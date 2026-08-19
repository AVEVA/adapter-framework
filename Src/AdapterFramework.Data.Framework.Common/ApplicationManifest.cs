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
using System.IO;
using System.Reflection;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;

namespace AdapterFramework.Data.Framework.Common;

public class ApplicationManifest : IApplicationManifest
{
    private const string AdapterFrameworkPrefix = "AdapterFramework";
    private const string SearchPattern = "*.dll";
    private static readonly object _lockObj = new();
    private static readonly HashSet<string> _availableComponents = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _edgeAdapterTypes = new(StringComparer.OrdinalIgnoreCase);
    private static Version _adapterFrameworkVersion;
    private readonly int? _applicationPort;
    private readonly string _baseApplicationAddress;

    public ApplicationManifest(int applicationPort, string baseApplicationAddress,
        string healthPrefix, string machineName, string serviceName, OmfVersion omfVersion) : this()
    {
        _applicationPort = applicationPort;
        _baseApplicationAddress = baseApplicationAddress;
        HealthPrefix = healthPrefix;
        MachineName = machineName;
        ServiceName = serviceName;
        OmfVersion = omfVersion;
    }

    public ApplicationManifest()
    {
        lock (_lockObj)
        {
            if (_availableComponents.Count < 1)
            {
                GetAvailableAdapterFrameworkComponentsAndFrameworkVersion(out var adapterFrameworkVersion);
                _adapterFrameworkVersion = adapterFrameworkVersion;
            }

            AdapterFrameworkVersion = _adapterFrameworkVersion;
            IsEdgeDataStore = HasComponent(EdgeSystemConstants.StorageComponentDllFullName);
            ProductVersion = Assembly.GetEntryAssembly()?.GetName().Version
                ?? typeof(ApplicationManifest).Assembly.GetName().Version;
        }
    }

    /// <inheritdoc/>
    public int ApplicationPort
    {
        get
        {
            if (!_applicationPort.HasValue)
            {
                throw new InvalidOperationException($"{nameof(ApplicationPort)} is not set.");
            }

            return _applicationPort.Value;
        }
    }

    /// <inheritdoc/>
    public string BaseApplicationAddress
    {
        get
        {
            if (string.IsNullOrEmpty(_baseApplicationAddress))
            {
                throw new InvalidOperationException($"{nameof(_baseApplicationAddress)} is not set.");
            }

            return _baseApplicationAddress;
        }
    }

    /// <inheritdoc/>
    public string MachineName { get; }

    /// <inheritdoc/>
    public string ServiceName { get; }

    /// <inheritdoc/>
    public OmfVersion OmfVersion { get; }

    /// <inheritdoc/>
    public string HealthPrefix { get; }

    /// <inheritdoc/>
    public Version AdapterFrameworkVersion { get; }

    /// <inheritdoc/>
    public Version ProductVersion { get; }

    /// <inheritdoc/>
    public bool IsEdgeDataStore { get; }

    /// <inheritdoc/>
    public bool HasComponent(string componentFullName)
    {
        return _availableComponents.Contains(componentFullName);
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetAdapterTypes()
    {
        return _edgeAdapterTypes;
    }
    
    private static bool TryGetAdapterType(string assemblyPath, out string adapterType)
    {
        adapterType = string.Empty;

        var adapterAssembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        foreach (var adapterAssemblyDefinedType in adapterAssembly.DefinedTypes)
        {
            if (typeof(IEdgeAdapter).IsAssignableFrom(adapterAssemblyDefinedType))
            {
                if (adapterAssemblyDefinedType.Namespace == null)
                {
                    return false;
                }

                adapterType = adapterAssemblyDefinedType.Namespace.Split(".")[^1];
                return true;
            }
        }

        return false;
    }

    private static void GetAvailableAdapterFrameworkComponentsAndFrameworkVersion(out Version adapterFrameworkVersion)
    {
        adapterFrameworkVersion = null;
        var assemblies = Directory.GetFiles(AppContext.BaseDirectory, SearchPattern);
        foreach (var assembly in assemblies)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assembly);

            if (assemblyName.StartsWith(AdapterFrameworkPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _availableComponents.Add(assemblyName);

                if (assemblyName.StartsWith(EdgeSystemConstants.AdapterDllNameBase, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAdapterType(assembly, out var adapterType))
                    {
                        _edgeAdapterTypes.Add(adapterType);
                    }
                }

                if (assemblyName.Equals(EdgeSystemConstants.AbstractionsDllFullName, StringComparison.OrdinalIgnoreCase))
                {
                    var abstractionsAssembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(assembly);
                    adapterFrameworkVersion = abstractionsAssembly.GetName().Version;
                }
            }
        }
    }
}
