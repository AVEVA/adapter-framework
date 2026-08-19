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
using AdapterFramework.Data.Framework.Abstractions.Common;
using AdapterFramework.Data.Framework.Abstractions.Configuration;
using AdapterFramework.Data.Framework.AdapterCommon.Discovery.Extensions;
using Xunit;

namespace AdapterFramework.Data.Framework.AdapterCommon.Tests.Discovery.Extensions;

public class DiscoveryStateExtensions_Tests
{
    private const int ItemsFound = 100;
    private const int NewItemsFound = 50;
    private const string ResultLink = "http://localhost/discovery/testsresults";
    private const string ErrorsString = "Something bad happened";

    private readonly DiscoveryState _discoveryState;

    public DiscoveryStateExtensions_Tests()
    {
        _discoveryState = new DiscoveryState();
    }

    [Fact]
    public void DiscoveryStateExtensions_Test_OnCompleted()
    {
        var endTime = DateTime.Now;
        _discoveryState.OnCompleted(endTime, ItemsFound, NewItemsFound, ResultLink);
        Assert.True(_discoveryState.Status.Equals(OperationStatus.Complete));
        Assert.Equal(_discoveryState.EndTime, endTime);
        Assert.Equal(ItemsFound, _discoveryState.ItemsFound);
        Assert.Equal(NewItemsFound, _discoveryState.NewItems);
        Assert.Equal(ResultLink, _discoveryState.ResultUri);
    }

    [Fact]
    public void DiscoveryStateExtensions_OnCompleted_Throws_On_Early_EndTime()
    {
        var startTime = DateTime.Now;
        var endTime = startTime.Subtract(TimeSpan.FromSeconds(1));

        _discoveryState.StartTime = startTime;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnCompleted(endTime, ItemsFound, NewItemsFound, ResultLink));
    }

    [Theory]
    [InlineData(-1, 50, ResultLink)]
    [InlineData(100, -1, ResultLink)]
    public void DiscoveryStateExtensions_OnCompleted_Throws_On_Negative_Values(int itemsFound, int newItems, string resultUri)
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnCompleted(endTime, itemsFound, newItems, resultUri));
    }

    [Theory]
    [InlineData(50, 100, ResultLink)]
    public void DiscoveryStateExtensions_OnCompleted_Throws_On_NewItemss_Greater_Than_ItemsFound(int itemsFound, int newItems, string resultUri)
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnCompleted(endTime, itemsFound, newItems, resultUri));
    }

    [Fact]
    public void DiscoveryStateExtensions_OnCompleted_Throws_On_Link_Is_Null()
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentNullException>(() => _discoveryState.OnCompleted(endTime, ItemsFound, NewItemsFound, null));
    }

    [Fact]
    public void DiscoveryStateExtensions_OnCompleted_Throws_On_Link_Is_Blank()
    {
        var endTime = DateTime.Now;
        var resultLink = string.Empty;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnCompleted(endTime, ItemsFound, NewItemsFound, resultLink));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void DiscoveryStateExtensions_OnCompleted_Throws_On_Link_Is_Whitespace(string resultLink)
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentException>(() => _discoveryState.OnCompleted(endTime, ItemsFound, NewItemsFound, resultLink));
    }

    [Theory]
    [InlineData("Operation canceled!")]
    public void DiscoveryStateExtensions_Test_OnCanceled(string errors)
    {
        var endTime = DateTime.Now;
        _discoveryState.OnCanceled(endTime, errors);
        Assert.True(_discoveryState.Status.Equals(OperationStatus.Canceled));
        Assert.Equal(_discoveryState.EndTime, endTime);
        Assert.Equal(_discoveryState.Errors, errors);
    }

    [Fact]
    public void DiscoveryStateExtensions_OnCanceled_Throws_On_Early_EndTime()
    {
        var startTime = DateTime.Now;
        var endTime = startTime.Subtract(TimeSpan.FromSeconds(1));

        _discoveryState.StartTime = startTime;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnCanceled(endTime, ErrorsString));
    }

    [Fact]
    public void DiscoveryStateExtensions_OnCanceled_Throws_On_Link_Is_Null()
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentNullException>(() => _discoveryState.OnCanceled(endTime, null));
    }

    [Fact]
    public void DiscoveryStateExtensions_OnCanceled_Throws_On_Errors_Is_Blank()
    {
        var errors = string.Empty;
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnCanceled(endTime, errors));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void DiscoveryStateExtensions_OnCanceled_Throws_On_Errors_Is_Whitespace(string errors)
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentException>(() => _discoveryState.OnCanceled(endTime, errors));
    }

    [Fact]
    public void DiscoveryStateExtensions_Test_OnFailed()
    {
        var endTime = DateTime.Now;
        _discoveryState.OnFailed(endTime, ErrorsString);
        Assert.True(_discoveryState.Status.Equals(OperationStatus.Failed));
        Assert.Equal(_discoveryState.EndTime, endTime);
        Assert.Equal(ErrorsString, _discoveryState.Errors);
    }

    [Fact]
    public void DiscoveryStateExtensions_OnFailed_Throws_On_Early_EndTime()
    {
        var startTime = DateTime.Now;
        var endTime = startTime.Subtract(TimeSpan.FromSeconds(1));

        _discoveryState.StartTime = startTime;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnFailed(endTime, ErrorsString));
    }

    [Fact]
    public void DiscoveryStateExtensions_OnFailed_Throws_On_Link_Is_Null()
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentNullException>(() => _discoveryState.OnFailed(endTime, null));
    }

    [Fact]
    public void DiscoveryStateExtensions_OnFailed_Throws_On_Errors_Is_Blank()
    {
        var errors = string.Empty;
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentOutOfRangeException>(() => _discoveryState.OnFailed(endTime, errors));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void DiscoveryStateExtensions_OnFailed_Throws_On_Errors_Is_Whitespace(string errors)
    {
        var endTime = DateTime.Now;
        Assert.Throws<ArgumentException>(() => _discoveryState.OnFailed(endTime, errors));
    }

    [Theory]
    [InlineData(OperationStatus.Complete)]
    [InlineData(OperationStatus.Canceled)]
    [InlineData(OperationStatus.Failed)]
    public void DiscoveryStateExtensions_Throw_If_Status_Already_Set(OperationStatus initialDiscoveryStatus)
    {
        var endTime = DateTime.Now;
        _discoveryState.Status = initialDiscoveryStatus;
        Assert.Throws<InvalidOperationException>(() => _discoveryState.OnCompleted(endTime, ItemsFound, NewItemsFound, ResultLink));
        Assert.Throws<InvalidOperationException>(() => _discoveryState.OnCanceled(endTime, ErrorsString));
        Assert.Throws<InvalidOperationException>(() => _discoveryState.OnFailed(endTime, ErrorsString));
        Assert.True(_discoveryState.Status.Equals(initialDiscoveryStatus));
    }
}
