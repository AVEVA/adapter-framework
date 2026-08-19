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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using AdapterFramework.Data.Framework.Abstractions.Components;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.Abstractions.Constants;
using AdapterFramework.Data.Framework.Abstractions.DataFlow;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.Services;
using AdapterFramework.Data.Framework.EgressComponent.Interfaces;
using AdapterFramework.Data.Framework.EgressComponent.Services;
using AdapterFramework.Data.Framework.EndpointManager;
using AdapterFramework.Data.Framework.Extensions;

namespace AdapterFramework.Data.Framework.EgressComponent.Extensions;

public static class EgressComponentExtensions
{
    public static void AddEgress(this IServiceCollection services, IComponentIdService componentIdService)
    {
        ThrowHelper.ThrowIfArgumentNull(componentIdService, nameof(componentIdService));

        var egressComponentId = componentIdService.GetEdgeComponentId(EdgeSystemConstants.OmfEgressComponentType);

        services.AddSingleton<IEgressComponentIdService>(new EgressComponentIdService(egressComponentId));
        services.AddSingleton<IMessageProcessor, DataMessageProcessor>();
        services.AddSingleton<IDiagnosticsMessageProcessor, DiagnosticsMessageProcessor>();
        services.AddSingleton<ISinkProvider, OmfEgressComponent>();
        services.AddSingleton<IOmfDataEndpointManager, OmfEndpointManager>();
        services.AddSingleton<IEdgeEventProvider, EdgeEventProvider>();
    }

    public static void UseEgress(this IApplicationBuilder app)
    {
        ThrowHelper.ThrowIfArgumentNull(app, nameof(app));

        var egressComponent = app.ApplicationServices.GetRequiredService<ISinkProvider>();
        
        egressComponent.InitializeAsync().GetAwaiter().GetResult();
    }
}
