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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdapterFramework.Data.Adapter.AdapterNamePlaceholder.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Administration;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Logging;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Security;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.AdapterCommon;

namespace AdapterFramework.Data.Adapter.AdapterNamePlaceholder;

/// <summary>
/// Main adapter class that handles data collection and processing for the AdapterNamePlaceholder adapter.
/// </summary>
public class AdapterMain : AdapterMainBase<DataSourceConfiguration, DataSelectionItem>
{
    #region Private Fields

    private bool _disposed;

    #endregion

    #region Public Constructor

    /// <summary>
    /// Adapter constructor - called when an adapter instance is created.
    /// </summary>
    /// <param name="logManager">A LogManager instance.</param>
    /// <param name="configurationProvider">A ConfigurationProvider instance.</param>
    /// <param name="messageProcessor">A data message processor instance.</param>
    /// <param name="applicationManifest">An application manifest instance.</param>
    /// <param name="runtimeConfigurationRegistry">The runtime ConfigurationRegistry.</param>
    /// <param name="edgeDataProtector">An EdgeDataProtector instance.</param>
    /// <param name="componentIdService">The component ID service.</param>
    /// <param name="healthMessageProcessor">An OMF Health Message Processor.</param>
    /// <param name="diagnosticsMessageProcessor">An OMF Diagnostics Message Processor.</param>
    /// <param name="runtimeAdministrationRegistry">The runtime Administration Registry.</param>
    public AdapterMain(ILogManager logManager,
        IConfigurationProvider configurationProvider,
        IMessageProcessor messageProcessor,
        IApplicationManifest applicationManifest,
        IRuntimeConfigurationRegistry runtimeConfigurationRegistry,
        IEdgeDataProtector edgeDataProtector,
        IComponentIdService componentIdService,
        IHealthMessageProcessor healthMessageProcessor,
        IDiagnosticsMessageProcessor diagnosticsMessageProcessor,
        IRuntimeAdministrationRegistry runtimeAdministrationRegistry)
        : base(logManager, configurationProvider, messageProcessor, applicationManifest, runtimeConfigurationRegistry, edgeDataProtector,
            componentIdService, healthMessageProcessor, diagnosticsMessageProcessor, runtimeAdministrationRegistry)
    {
        EnableScheduling = false;
    }

    #endregion

    #region Public Fields

    /// <inheritdoc/>
    public override string ComponentType => AdapterConstants.ComponentType;

    #endregion

    #region Protected Methods

    /// <summary>
    /// This method is called by the adapter framework to register an adapter instance. Custom configuration facets if any must be registered in this method.
    /// </summary>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    /// <remarks>This method can be called multiple times when a new adapter instance is added to the application.</remarks>
    protected override Task RegisterAdapterAsync(CancellationToken cancellationToken)
    {
        CommonService.DefaultStreamIdGenerator.SetDefaultStreamIdPattern(AdapterConstants.DefaultStreamIdPattern, AdapterConstants.DefaultStreamIdKeywords);

        return Task.CompletedTask;
    }

    /// <summary>
    /// This method is automatically called by the adapter framework to initialize an adapter.
    /// </summary>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected override Task InitializeAdapterAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when the adapter should start.
    /// </summary>
    /// <param name="dataSourceConfiguration">Data source configuration.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected override async Task StartAdapterAsync(DataSourceConfiguration dataSourceConfiguration, CancellationToken cancellationToken)
    {
        // TODO: Remove the line below after the implementation is in place. 
        await Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when the adapter should stop.
    /// </summary>
    /// <param name="dataSourceConfiguration">Data source configuration. Can be null when adapter is stopped due to data source deletion.</param>
    /// <param name="cancellationToken">Token used to propagate notification of a cancellation of this operation.</param>
    /// <returns>A task instance.</returns>
    protected override async Task StopAdapterAsync(DataSourceConfiguration dataSourceConfiguration, CancellationToken cancellationToken)
    {
        // TODO: Remove the line below after the implementation is in place. 
        await Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when the data source configuration has been updated.
    /// </summary>
    /// <param name="oldValue">Old configuration.</param>
    /// <param name="newValue">New Configuration.</param>
    /// <returns>A Task instance.</returns>
    protected override async Task ProcessDataSourceUpdateAsync(DataSourceConfiguration oldValue, DataSourceConfiguration newValue)
    {
        // TODO: Implement the logic to handle update of data source configuration.

        // TODO: Remove the line below after the implementation is in place. 
        await Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when the data selection items have been updated.
    /// </summary>
    /// <param name="oldValue">Old configuration values.</param>
    /// <param name="newValue">New configuration values</param>
    /// <returns>A Task instance.</returns>
    protected override async Task ProcessSelectionUpdateAsync(DataSelectionItem[] oldValue, DataSelectionItem[] newValue)
    {
        if (newValue == null)
        {
            // TODO: Implement the logic to handle deletion of data selection items.
        }
        else
        {
            // TODO: Implement the logic to handle update of data selection items.
        }

        // TODO: Remove the line below after the implementation is in place. 
        await Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when the general configuration has been updated.
    /// </summary>
    /// <param name="oldValue">Old configuration values.</param>
    /// <param name="newValue">New configuration values.</param>
    /// <returns>A Task instance.</returns>
    protected override Task ProcessGeneralConfigurationUpdateAsync(IAdapterGeneralConfiguration oldValue, IAdapterGeneralConfiguration newValue)
    {
        // TODO: Implement the logic to handle update of general configuration. Currently only has EnableMetadata option. Should resend types/streams (if possible) when swapped to true.
        return Task.CompletedTask;
    }

    /// <summary>
    /// This method is called by the adapter framework when default stream ID is requested during the data selection configuration pre-validation callback.
    /// </summary>
    /// <param name="selectionItem">Item of <see cref="DataSelectionItem" /> type to generate the default stream ID for.</param>
    /// <returns>Default stream ID string for <paramref name="selectionItem"/>.</returns>
    protected override string GetDefaultStreamId(DataSelectionItem selectionItem)
    {
        // TODO: Implement logic to get default stream ID using the DefaultStreamIdGenerator
        // return CommonService.DefaultStreamIdGenerator.GetDefaultStreamId(selectionItem.PropertyValue1, selectionItem.PropertyValue2);
        throw new NotImplementedException("Use DefaultStreamIdGenerator to return default stream ID for the item.");
    }

    /// <summary>
    /// This method is called by the adapter framework when the help message for configuring data source is requested. 
    /// </summary>
    /// <returns>The help message to configure a valid data source.</returns>
    protected override string GetDataSourceHelpInfo()
    {
        return $@"{GetCommandlineHelpHeader(CommonConstants.DataSourceConfigurationName)}
{nameof(DataSourceConfiguration.StreamIdPrefix)}              [Optional] The stream ID prefix applied to all data items collected from the data source. If not configured, the default value will be {ComponentId}.
{nameof(DataSourceConfiguration.DefaultStreamIdPattern)}      [Optional] Specifies the default stream Id pattern to use. Possible parameters: {string.Join(", ", AdapterConstants.DefaultStreamIdKeywords.Select(x => $"{{{x}}}"))}
";
    }

    /// <summary>
    /// This method is called by the adapter framework when the help message for configuring data selections is requested. 
    /// </summary>
    /// <returns>The help message to configure valid data selection items.</returns>
    protected override string GetDataSelectionHelpInfo()
    {
        return GetCommandlineHelpHeader(CommonConstants.DataSelectionConfigurationName) + $@"
{nameof(DataSelectionItem.Selected)}     [Optional] The indicator of whether the data item is selected or not. 
{nameof(DataSelectionItem.Name)}         [Optional] The optional friendly name of the data item collected from the data source. If not configured, the default value will be the stream ID.
{nameof(DataSelectionItem.StreamId)}     [Optional] The stream ID of the data item. If not configured, the default stream ID will be generated.

Note: You can configure data selection items by typing in the properties and their values or by importing a .json file.
";
    }

    /// <summary>
    /// Provides an opportunity to dispose of any disposable objects created by the adapter.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // TODO: Implement the logic to dispose of any managed objects created by the adapter.
        }

        _disposed = true;

        // Call base class implementation.
        base.Dispose(disposing);
    }

    #endregion
}
