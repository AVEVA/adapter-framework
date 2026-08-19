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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.Diagnostics;
using AdapterFramework.Data.Framework.Abstractions.Failover;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.Extensions;
using AdapterFramework.Data.Framework.Failover.Diagnostics;

namespace AdapterFramework.Data.Framework.Failover.Health;

public class FailoverDiagnosticsService : IEdgeComponentDiagnosticsService
{
    private readonly FailoverDiagnosticsOmfMessageCreator _failoverDiagnosticsOmfMessageCreator;
    private readonly IDiagnosticsMessageProcessor _diagnosticsMessageProcessor;
    private readonly object _failoverStatusLock = new();
    private readonly ILogger _logger;

    private FailoverState _failoverState;
    private bool _disposed;

    public FailoverDiagnosticsService(
        IDiagnosticsMessageProcessor diagnosticsMessageProcessor,
        FailoverState failoverState,
        ILogger logger,
        LinkNode parentNode,
        IApplicationManifest applicationManifest)
    {
        ThrowHelper.ThrowIfArgumentNull(applicationManifest, nameof(applicationManifest));

        _diagnosticsMessageProcessor = diagnosticsMessageProcessor;
        _failoverState = failoverState;
        _logger = logger;
        _failoverDiagnosticsOmfMessageCreator = new FailoverDiagnosticsOmfMessageCreator(applicationManifest, parentNode);
    }

    #region Public methods

    public Task InitializeAsync()
    {
        CreateFailoverDiagnosticsTypesStreams();
        SendFailoverStatusEvent();

        _logger.LogDebug("{ServiceName} is initialized.", nameof(FailoverDiagnosticsService));
        return Task.CompletedTask;
    }

    public void ResendTypesAndStreams()
    {
        CreateFailoverDiagnosticsTypesStreams();
        lock (_failoverStatusLock)
        {
            SendFailoverStatusEvent();
        }
    }

    public void ResendLinks()
    {
        _failoverDiagnosticsOmfMessageCreator.SendLinks(_diagnosticsMessageProcessor);
    }

    public void SendFailoverState(FailoverState failoverState)
    {
        ThrowHelper.ThrowIfArgumentNull(failoverState, nameof(failoverState));

        lock (_failoverStatusLock)
        {
            if (_failoverState == null || failoverState.FailoverScore != _failoverState.FailoverScore
                || failoverState.Role != _failoverState.Role)
            {
                _failoverState = failoverState;
                SendFailoverStatusEvent();
            }
        }
    }

    public void ResetFailoverState()
    {
        lock (_failoverStatusLock)
        {
            if (_failoverState != null)
            {
                _failoverState = null;
                ResetFailoverStatusEvent();
            }
        }
    }

    public Task StartAsync()
    {
        _logger.LogDebug("{ServiceName} is started", nameof(FailoverDiagnosticsService));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_failoverStatusLock)
        {
            _failoverState = null;
        }

        _logger.LogDebug("{ServiceName} is Stopped.", nameof(FailoverDiagnosticsService));

        return Task.CompletedTask;
    }

    #endregion

    #region Disposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _disposed = true;
    }

    #endregion

    #region Private Methods

    private void SendFailoverStatusEvent()
    {
        if (_failoverState == null)
        {
            return;
        }

        var failoverStatusValue = new FailoverStatusEvent
        {
            Timestamp = DateTime.UtcNow,
            FailoverScore = _failoverState.FailoverScore,
            FailoverRole = _failoverState.Role.ToString(),
        };

        SendFailoverStatusEventInternal(failoverStatusValue);
    }

    private void ResetFailoverStatusEvent()
    {
        var failoverStatusValue = new FailoverStatusEvent
        {
            Timestamp = DateTime.UtcNow,
            FailoverScore = 0,
            FailoverRole = string.Empty,
        };

        SendFailoverStatusEventInternal(failoverStatusValue);
    }

    private void SendFailoverStatusEventInternal(FailoverStatusEvent failoverStatusEvent)
    {
        try
        {
            _diagnosticsMessageProcessor.WriteDiagnosticsValue(_failoverDiagnosticsOmfMessageCreator.GetFailoverStatusStreamId(),
                Classification.Dynamic, failoverStatusEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process FailoverStatus diagnostics event.");
        }
    }

    private void CreateFailoverDiagnosticsTypesStreams()
    {
        try
        {
            _failoverDiagnosticsOmfMessageCreator.CreateAndSendStructure(_diagnosticsMessageProcessor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process failover diagnostics containers and types.");
        }
    }

    #endregion
}
