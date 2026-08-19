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
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Common.Helpers;
using AdapterFramework.Data.Framework.ConfigurationProvider;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Host.Configuration;
using AdapterFramework.Data.Framework.Host.Helpers;
using AdapterFramework.Data.Framework.Logger;
using IConfigurationProvider = AdapterFramework.Data.Framework.Abstractions.Configuration.IConfigurationProvider;

[assembly: InternalsVisibleTo("AdapterFramework.Data.Framework.Host.Tests")]
namespace AdapterFramework.Data.Framework.Host;

public static class SystemHost
{
    private const string AspNetLogSourceName = "ASP.Net Core Platform";
    private const string DefaultServiceName = "AVEVA Adapter Framework";
    private const long MaxRequestBodySizeBytes = 64_000_000;
    private static IConfiguration _configuration;
    private static string _loggingDirectoryPath;
    private static ILogger _aspNetLogger;

    public static bool EnforceBetaTimeout { get; private set; }

    public static DateTime? BetaTimeoutDate { get; private set; }

    public static string ApplicationDataDirectory { get; private set; }

    public static int Port { get; private set; }

    public static string BaseAddress { get; private set; }

    public static string MachineName { get; private set; }

    public static OmfVersion OmfVersion { get; private set; }

    public static async Task RunAsync(string[] args, bool enforceBetaTimeout, DateTime? betaTimeoutDate = null, OmfVersion omfVersion = OmfVersion.Omf12)
    {
        ThrowHelper.ThrowIfArgumentNull(args, nameof(args));

        EnforceBetaTimeout = enforceBetaTimeout;
        BetaTimeoutDate = betaTimeoutDate;
        OmfVersion = omfVersion;

        _configuration = CommandLineArgumentHelper.CreateConfiguration(args, out var configErrorMessage);
        if (_configuration == null)
        {
            Console.WriteLine(configErrorMessage);
            return;
        }

        ApplicationDataDirectory = _configuration.GetValue<string>(ConfigurationConstants.ApplicationDataDirectoryKey);
        MachineName = _configuration.GetValue<string>(ConfigurationConstants.DeviceNameKey) ?? SystemInformationResolver.GetMachineName();

        var configurationProvider = new JsonConfigurationProvider(ApplicationDataDirectory);
        _loggingDirectoryPath = GetLoggingDirectoryPath(configurationProvider);

        var hostBuilder = CreateHostBuilder(args);
        try
        {
            switch (Environment.OSVersion.Platform)
            {
                case PlatformID.Win32NT:
                    // Setting service name as a workaround to avoid occasional ungraceful shutdown.
                    // https://github.com/dotnet/runtime/issues/62579
                    await hostBuilder.UseWindowsService(
                            windowsServiceOptions => windowsServiceOptions.ServiceName = DefaultServiceName)
                        .Build().RunAsync();
                    break;
                case PlatformID.Unix:
                    await hostBuilder.UseSystemd().Build().RunAsync();
                    break;
                default:
                    _aspNetLogger?.LogWarning("The application doesn't support running on '{Platform}' platform as a service. The application starts interactively.",
                        Environment.OSVersion.Platform);

                    await hostBuilder.Build().RunAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            if (ex.InnerException is AddressInUseException)
            {
                // We don't want to restart in this case, so the exception is only logged.
                _aspNetLogger?.LogCritical(ex.InnerException, "Specified port is already in use.");
                return;
            }

            _aspNetLogger?.LogCritical(ex, "Unexpected exception occurred.");
            throw;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        Port = EdgeSystemHelper.GetPortNumber(_configuration);
        BaseAddress = string.Format(CultureInfo.InvariantCulture, ConfigurationConstants.BaseUri, Port);

        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                // see here: https://docs.microsoft.com/en-us/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-3.1
                // and here: https://andrewlock.net/introducing-ihostlifetime-and-untangling-the-generic-host-startup-interactions/
                // for information regarding host startup/shutdown issues.
                services.Configure<HostOptions>(option =>
                {
                    option.ShutdownTimeout = TimeSpan.FromMinutes(5);
                });
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.UseContentRoot(Path.GetDirectoryName(AppContext.BaseDirectory) ?? string.Empty);
                webBuilder.UseUrls(string.Format(CultureInfo.InvariantCulture, ConfigurationConstants.BaseUri, EdgeSystemHelper.GetPortNumber(_configuration)));
                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.Limits.MaxRequestBodySize = MaxRequestBodySizeBytes;
                });
                webBuilder.ConfigureLogging((logging) =>
                {
                    logging.ClearProviders();
                    var loggerConfiguration = new LoggerConfiguration()
                    {
                        LogLevel = LogLevel.Warning,
                    };

                    _aspNetLogger = new DataFrameworkLogger(AspNetLogSourceName, loggerConfiguration, _loggingDirectoryPath);
                    logging.AddProvider(new EdpAspNetLoggingProvider(_aspNetLogger));
                });
            });
    }

    private static string GetLoggingDirectoryPath(IConfigurationProvider configurationProvider)
    {
        return Path.Combine(configurationProvider.GetCommonApplicationDataDirectoryPath(), EdgeSystemConstants.LoggingDirectoryName, " ").TrimEnd();
    }
}
