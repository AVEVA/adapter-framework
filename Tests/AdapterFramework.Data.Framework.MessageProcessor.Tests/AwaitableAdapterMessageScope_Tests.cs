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
using System.Threading.Tasks;
using AdapterFramework.Data.DataModel;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing;
using AdapterFramework.Data.Framework.Abstractions.MessageProcessing.Awaitable;
using AdapterFramework.Data.Framework.Abstractions.Messages;
using AdapterFramework.Data.Framework.AdapterCommon;
using AdapterFramework.Data.Framework.Messages.Awaitable;
using AdapterFramework.Data.Framework.MessageProcessor.Tests.DataFilters.Common;
using Moq;
using Xunit;

namespace AdapterFramework.Data.Framework.MessageProcessor.Tests;

/// <summary>
/// Tests how <see cref="AdapterMessageProcessor"/> creates awaitable scopes and carries the scope token down the processor chain.
/// </summary>
public class AwaitableAdapterMessageScope_Tests
{
    /// <summary>
    /// Verifies that creating an awaitable scope throws <see cref="NotSupportedException"/> when the wrapped processor doesn't support scopes.
    /// </summary>
    [Fact]
    public void TryCreateAwaitableScope_WhenChainDoesNotSupportScopes_ThrowsNotSupported()
    {
        var processor = new AdapterMessageProcessor(new Mock<IMessageProcessor>().Object, OmfVersion.Omf20);

        Assert.Throws<NotSupportedException>(() => processor.TryCreateAwaitableScope(new OmfAwaitableScopeOptions(), out _));
    }

    /// <summary>
    /// Verifies that writes through an awaitable scope reach the scoped processor with the scope's token instead of the unscoped overloads,
    /// and that a write after the scope is sealed throws <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public async Task ScopedWrites_WhenChainSupportsScopes_ForwardScopeToken()
    {
        using var coordinator = new OmfAwaitableCoordinator();
        coordinator.SetOmfVersion(OmfVersion.Omf20);
        Assert.True(coordinator.TryCreateScope(new OmfAwaitableScopeOptions(), out var state));
        ScopeToken token = state;
        var mockProcessor = new Mock<IMessageProcessor>();
        var mockScopedProcessor = mockProcessor.As<IScopedMessageProcessor>();
        mockScopedProcessor.Setup(processor => processor.TryCreateScope(It.IsAny<OmfAwaitableScopeOptions>(), out token)).Returns(true);
        var processor = new AdapterMessageProcessor(mockProcessor.Object, OmfVersion.Omf20);
        var dataType = new StaticDataType { Id = "type", Properties = new Dictionary<string, PropertyDefinition>() };
        var value = new TimeIndexedValue<int> { Timestamp = DateTime.UtcNow, Value = 1 };

        Assert.True(processor.TryCreateAwaitableScope(new OmfAwaitableScopeOptions(), out var scope));
        scope.WriteType(dataType);
        scope.WriteDynamicValue(DataFilterTestHelper.GetSelectionItem<int>(), value);

        mockScopedProcessor.Verify(p => p.WriteType(dataType, MessageAction.Default, state), Times.Once);
        mockScopedProcessor.Verify(p => p.WriteDynamicValue(It.IsAny<string>(), value, MessageAction.Default, null, state), Times.Once);
        mockProcessor.Verify(p => p.WriteDynamicValue(It.IsAny<string>(), It.IsAny<TimeIndexedValue<int>>(), It.IsAny<MessageAction>(), It.IsAny<PartitionKey?>()), Times.Never);

        scope.Seal();
        Assert.Throws<InvalidOperationException>(() => scope.WriteType(dataType));
        await scope.DisposeAsync();
        await state.DisposeAsync();
    }
}
