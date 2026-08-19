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
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.ComponentIdProvider;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Extensions;
using SystemRuntime = System.Runtime;

namespace AdapterFramework.Data.Framework.Host.Helpers;

internal static class Services
{
    /// <summary>
    /// Checks whether the component type string is Edge Data Store.
    /// </summary>
    /// <param name="componentType">Component type string.</param>
    /// <returns>True when <paramref name="componentType"/> is Storage. False otherwise.</returns>
    public static bool IsStorage(string componentType)
    {
        return componentType.Equals(EdgeSystemConstants.StorageComponentType, StringComparison.InvariantCultureIgnoreCase) ||
               componentType.Equals(EdgeSystemConstants.StorageOldComponentType, StringComparison.InvariantCultureIgnoreCase);
    }

    /// <summary>
    /// Checks whether the component type sting is OMF Egress component.
    /// </summary>
    /// <param name="componentType">Component type string.</param>
    /// <returns>True when <paramref name="componentType"/> is OMF Egress. False otherwise.</returns>
    public static bool IsOmfEgress(string componentType)
    {
        return componentType.Equals(EdgeSystemConstants.OmfEgressComponentType, StringComparison.InvariantCultureIgnoreCase);
    }

    internal static IEnumerable<Type> GetSinkProviderImplementations(JsonConfigurationProvider configurationProvider, ILogger edgeSystemLogger,
        IApplicationManifest applicationManifest)
    {
        if (!configurationProvider.TryGetConfiguration<EdgeComponentConfig[]>(EdgeSystemConstants.SystemComponentId,
            EdgeSystemConstants.ComponentsFacetName, out var edgeComponentsConfiguration, out var getErrors))
        {
            edgeComponentsConfiguration = ComponentIdService.CreateDefaultComponentConfiguration(applicationManifest);
            if (getErrors.IsNullOrEmpty())
            {
                if (!configurationProvider.TrySaveConfiguration(EdgeSystemConstants.SystemComponentId,
                    EdgeSystemConstants.ComponentsFacetName, edgeComponentsConfiguration, out var saveErrors))
                {
                    edgeSystemLogger.LogError(EdgeSystemConstants.FailedToSaveConfigurationMessage, EdgeSystemConstants.ComponentsFacetName, saveErrors);
                }
            }
            else
            {
                edgeSystemLogger.LogWarning(EdgeSystemConstants.ConfigurationInvalidMessage, EdgeSystemConstants.ComponentsFacetName, getErrors);
            }
        }

        var assembliesToRegister = new List<Assembly>();
        foreach (var component in edgeComponentsConfiguration)
        {
            string sinkProviderComponentPath;
            if (IsStorage(component.ComponentType))
            {
                sinkProviderComponentPath = Path.Combine(configurationProvider.GetBaseDirectoryPath(),
                    EdgeSystemConstants.StorageComponentDllFullName + EdgeSystemConstants.DllExtension);
            }
            else if (IsOmfEgress(component.ComponentType))
            {
                sinkProviderComponentPath = Path.Combine(configurationProvider.GetBaseDirectoryPath(),
                    EdgeSystemConstants.EgressComponentDllFullName + EdgeSystemConstants.DllExtension);
            }
            else
            {
                continue;
            }

            if (File.Exists(sinkProviderComponentPath))
            {
                var assemblyToRegister = SystemRuntime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(sinkProviderComponentPath);
                assembliesToRegister.Add(assemblyToRegister);

                LogLoadedEdgeComponentMessage(edgeSystemLogger, component, assemblyToRegister);
                break;
            }
        }

        return assembliesToRegister.SelectMany(asm => asm.DefinedTypes)
            .Where(t => typeof(ISinkProvider).IsAssignableFrom(t))
            .Select(x => x.AsType());
    }

    internal static IEnumerable<Type> GetComponentImplementations<T>(JsonConfigurationProvider configurationProvider, string dllNameBase,
        ILogger edgeSystemLogger)
    {
        var baseDirectoryPath = configurationProvider.GetBaseDirectoryPath();
        var assemblies = Directory.GetFiles(baseDirectoryPath, "*.dll");
        var componentImplementations = new List<Type>();

        foreach (var assemblyPath in assemblies)
        {
            var assemblyFileName = Path.GetFileNameWithoutExtension(assemblyPath);

            // 1. DLLs matching the current dllNameBase prefix (e.g. AdapterFramework.Data.Adapter.*)
            var matchesKnownPrefix = assemblyFileName.StartsWith(dllNameBase, StringComparison.InvariantCultureIgnoreCase);

            // 2. Type-scan fallback: legacy adapters are scanned for IEdgeAdapter implementations
            var isLegacyAdapterCandidate = !matchesKnownPrefix &&
                typeof(T) == typeof(IEdgeAdapter);

            if (!matchesKnownPrefix && !isLegacyAdapterCandidate)
            {
                continue;
            }

            Assembly assemblyToRegister;
            try
            {
                assemblyToRegister = SystemRuntime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
            catch (Exception ex) when (isLegacyAdapterCandidate)
            {
                continue;
            }

            IEnumerable<Type> concreteTypeImplementations;
            try
            {
                concreteTypeImplementations = assemblyToRegister.DefinedTypes.Where(t => typeof(T).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract).Select(x => x.AsType()).ToList();
            }
            catch (ReflectionTypeLoadException ex) when (isLegacyAdapterCandidate)
            {
                continue;
            }

            if (isLegacyAdapterCandidate && !concreteTypeImplementations.Any())
            {
                continue;
            }

            foreach (var implementationType in concreteTypeImplementations)
            {
                componentImplementations.Add(implementationType);
                LogComponentTypeAndVersion(edgeSystemLogger, assemblyFileName, assemblyToRegister);
            }
        }

        return componentImplementations;
    }

    internal static void AddSingleton<T>(IServiceCollection services, IEnumerable<Type> implementations)
        where T : IEdgeComponent
    {
        foreach (var componentImplementation in implementations)
        {
            services.AddSingleton(typeof(T), componentImplementation);
        }
    }

    internal static void AddTransient<T>(IServiceCollection services, IEnumerable<Type> implementations)
        where T : IEdgeComponent
    {
        foreach (var componentImplementation in implementations)
        {
            services.AddTransient(typeof(T), componentImplementation);
        }
    }

    internal static void Add(IServiceCollection services, IEnumerable<Type> implementations,
        IReadOnlyDictionary<Type, object> componentRegistrationDependencies)
    {
        foreach (var implementation in implementations)
        {
            var addComponentMethod = implementation.GetMethod(EdgeSystemConstants.AddComponentMethodName, BindingFlags.Static | BindingFlags.Public);
            if (addComponentMethod != null)
            {
                addComponentMethod.Invoke(null, new object[] { services, componentRegistrationDependencies });
            }
        }
    }

    internal static void Use(IApplicationBuilder app, IEnumerable<Type> implementations)
    {
        foreach (var componentImplementation in implementations)
        {
            var useComponentMethod = componentImplementation.GetMethod(EdgeSystemConstants.UseComponentMethodName,
                BindingFlags.Static | BindingFlags.Public);

            if (useComponentMethod != null)
            {
                useComponentMethod.Invoke(null, new object[] { app });
            }
        }
    }

    private static void LogComponentTypeAndVersion(ILogger edgeSystemLogger, string assemblyFileName, Assembly assemblyToRegister)
    {
        try
        {
            var startIndex = assemblyFileName.LastIndexOf('.') + 1;
            var componentType = assemblyFileName[startIndex..];

            edgeSystemLogger.LogInformation("Component type: {ComponentType} version: {Version} has been registered.",
                componentType, assemblyToRegister.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        }
        catch (Exception ex)
        {
            edgeSystemLogger.LogWarning(ex, "Unable to get component type and version.");
        }
    }

    /// <summary>
    /// Logs operational information message containing basic information about loaded <see cref="IEdgeComponent"/>.
    /// </summary>
    /// <param name="edgeSystemLogger">Edge System logger instance.</param>
    /// <param name="component">Configuration entry for the component.</param>
    /// <param name="assemblyToRegister">Component assembly to be registered.</param>
    private static void LogLoadedEdgeComponentMessage(ILogger edgeSystemLogger, EdgeComponentConfig component, Assembly assemblyToRegister)
    {
        edgeSystemLogger.LogInformation("Component type: {ComponentType} ID: '{ComponentId}' version: {ComponentVersion} has been loaded.",
            component.ComponentType, component.ComponentId,
            assemblyToRegister.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }
}
