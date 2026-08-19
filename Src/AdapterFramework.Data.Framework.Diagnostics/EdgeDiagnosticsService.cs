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
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.Diagnostics;

public class EdgeDiagnosticsService : IEdgeDiagnosticsService
{
    #region Private Constants

    private const int MinutesBetweenStorageCrawl = 1;
    private const int DiagnosticsUpdateInterval = 60 * 1000;
    private const double MegaByteUnit = 1_000_000;

    #endregion

    #region Private Fields

    private readonly string _storageLocation;
    private readonly CancellationTokenSource _storageCancellationTokenSource;
    private readonly ILogger _logger;

    private readonly IApplicationManifest _applicationManifest;
    private readonly bool _hasStorageComponent;

    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly object _latestCollectedLock = new object();
    private readonly SemaphoreSlim _stateChangeSemaphore = new SemaphoreSlim(1, 1);
    private System.Timers.Timer _updateDiagnosticsTimer;
    private EdgeDiagnosticsEvent _latestCollected;
    private bool _typeAndStreamCreated;

    private long _storageTotalSize;                 // access via Interlock methods
    private long _storageFree;                      // access via Interlock methods
    private bool _disposed;

    #endregion

    #region Public Constructor

    public EdgeDiagnosticsService(ILogger logger, IConfigurationProvider configurationProvider, IApplicationManifest applicationManifest, IDiagnosticsMessageProcessor diagnosticsMessageProcessor = null)
    {
        ThrowHelper.ThrowIfArgumentNull(logger, nameof(logger));
        ThrowHelper.ThrowIfArgumentNull(configurationProvider, nameof(configurationProvider));
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        _logger = logger;
        _applicationManifest = applicationManifest;
        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;

        _storageLocation = configurationProvider.GetCommonApplicationDataDirectoryPath();

        _storageCancellationTokenSource = new CancellationTokenSource();
        _hasStorageComponent = applicationManifest.IsEdgeDataStore;

        Task.Run(async () => await CrawlStorageForMetrics());
    }

    #endregion

    #region Public Methods

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        try
        {
            _updateDiagnosticsTimer = new System.Timers.Timer(DiagnosticsUpdateInterval)
            {
                AutoReset = false,
            };

            _updateDiagnosticsTimer.Elapsed += (o, e) => UpdateDiagnostics();

            CreateDiagnosticsTypesStreams();

            CreateDiagnosticsLinks();
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_typeAndStreamCreated)
            {
                UpdateDiagnostics();

                _updateDiagnosticsTimer.Start();
            }
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stateChangeSemaphore.WaitAsync(cancellationToken);
        try
        {
            _updateDiagnosticsTimer?.Stop();
        }
        finally
        {
            _stateChangeSemaphore.Release();
        }
    }

    public EdgeDiagnosticsEvent Collect()
    {
        lock (_latestCollectedLock)
        {
            if (_latestCollected != null && DateTime.UtcNow - _latestCollected.Timestamp < TimeSpan.FromSeconds(10))
            {
                return _latestCollected;
            }

            _latestCollected = CollectLatest();

            return _latestCollected;
        }
    }

    public void ResendTypesAndStreams()
    {
        CreateDiagnosticsTypesStreams();

        CreateDiagnosticsLinks();
    }

    public void UpdateDiagnostics()
    {
        if (TrySendDiagnosticsEvent())
        {
            if (_updateDiagnosticsTimer != null)
            {
                _updateDiagnosticsTimer.Enabled = true;
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Protected Methods

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _storageCancellationTokenSource.Cancel(false);
            _storageCancellationTokenSource.Dispose();
            _stateChangeSemaphore?.Dispose();
            _updateDiagnosticsTimer?.Dispose();
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods
    
    private bool TrySendDiagnosticsEvent()
    {
        try
        {
            _diagnosticsMessageProcessor?.WriteDiagnosticsValue(
                DiagnosticsOmfMessageCreator.GetSystemDiagnosticsDataStreamId(_applicationManifest),
                Classification.Dynamic, CollectLatest());

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process diagnostics event. Stopping diagnostics data collection.");

            _updateDiagnosticsTimer.Stop();

            return false;
        }
    }

    private EdgeDiagnosticsEvent CollectLatest()
    {
        var evt = new EdgeDiagnosticsEvent { Timestamp = DateTime.UtcNow };

        using (var currentProcess = Process.GetCurrentProcess())
        {
            // process specific
            evt.ProcessIdentifier = currentProcess.Id;
            evt.StartTime = currentProcess.StartTime.ToUniversalTime();
            evt.TotalProcessorTime = currentProcess.TotalProcessorTime.TotalSeconds;
            evt.TotalUserProcessorTime = currentProcess.UserProcessorTime.TotalSeconds;
            evt.TotalPrivilegedProcessorTime = currentProcess.PrivilegedProcessorTime.TotalSeconds;
            evt.HandleCount = currentProcess.HandleCount;
            evt.ThreadCount = currentProcess.Threads.Count;

            if (currentProcess.WorkingSet64 != 0)
            {
                evt.WorkingSet = currentProcess.WorkingSet64 / MegaByteUnit;
            }

            if (currentProcess.PrivateMemorySize64 != 0)
            {
                evt.PrivateMemorySize = currentProcess.PrivateMemorySize64 / MegaByteUnit;
            }

            if (currentProcess.PeakPagedMemorySize64 != 0)
            {
                evt.PeakPagedMemorySize = currentProcess.PeakPagedMemorySize64 / MegaByteUnit;
            }
        }

        var managedMemoryBytes = GC.GetTotalMemory(false);
        if (managedMemoryBytes != 0)
        {
            evt.ManagedMemorySize = managedMemoryBytes / MegaByteUnit;
        }

        // storage specific
        var storageTotalSize = Interlocked.Read(ref _storageTotalSize);
        if (storageTotalSize != 0)
        {
            evt.StorageTotalSize = storageTotalSize / MegaByteUnit;
        }

        var storageFreeSpace = Interlocked.Read(ref _storageFree);
        if (storageFreeSpace != 0)
        {
            evt.StorageFreeSpace = storageFreeSpace / MegaByteUnit;
        }

        return evt;
    }

    private async Task CrawlStorageForMetrics()
    {
        var cancellationToken = _storageCancellationTokenSource.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var dirInfo = new DirectoryInfo(_storageLocation);
                if (dirInfo.Exists)
                {
                    var storageDriveInfo = new DriveInfo(Path.GetPathRoot(_storageLocation));

                    Interlocked.Exchange(ref _storageTotalSize, storageDriveInfo.TotalSize);
                    Interlocked.Exchange(ref _storageFree, storageDriveInfo.TotalFreeSpace);
                }
                else
                {
                    _logger.LogWarning(
                        "Storage folder is missing; unable to determine storage metrics: {StorageLocation}.",
                        _storageLocation);
                }

                await Task.Delay(TimeSpan.FromMinutes(MinutesBetweenStorageCrawl), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Exception determining storage metrics: {_storageLocation}.");
            }
        }
    }

    private void CreateDiagnosticsTypesStreams()
    {
        try
        {
            if (_diagnosticsMessageProcessor != null)
            {
                var systemDiagnosticsTypes = new[] { DiagnosticsOmfMessageCreator.GetSystemDiagnosticsDataType() };

                _diagnosticsMessageProcessor.WriteDiagnosticsTypes(systemDiagnosticsTypes);

                var systemDiagnosticsStreams = new[] { DiagnosticsOmfMessageCreator.GetSystemDiagnosticsDataStream(_applicationManifest) };

                _diagnosticsMessageProcessor.WriteDiagnosticsStreams(systemDiagnosticsStreams);

                _typeAndStreamCreated = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create diagnostics type and stream. Diagnostics data won't be collected.");
        }
    }

    private void CreateDiagnosticsLinks()
    {
        try
        {
            if (_diagnosticsMessageProcessor != null)
            {
                foreach (var item in DiagnosticsOmfMessageCreator.GetLinks(_applicationManifest, _hasStorageComponent))
                {
                    _diagnosticsMessageProcessor.WriteDiagnosticsValue(item.Item1, item.Item2, item.Item3);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create diagnostics AF Links.");
        }
    }

    #endregion
}
