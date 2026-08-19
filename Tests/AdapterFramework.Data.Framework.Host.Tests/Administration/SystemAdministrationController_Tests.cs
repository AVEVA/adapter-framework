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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AdapterFramework.Data.Framework.Host.Administration;
using AdapterFramework.Data.Framework.Registry;
using Xunit;

namespace AdapterFramework.Data.Framework.Host.Tests.Administration;

public class SystemAdministrationController_Tests
{
    private const string ComponentId = "UnitTestComponent";
    private const string CallbackFunctionName = "SampleFunction";

    private bool _callbackFunctionCalled;

    public SystemAdministrationController_Tests()
    {
        _callbackFunctionCalled = false;
    }

    [Fact]
    public async Task ExecuteCallbackFunction_ComponentId_NotFound()
    {
        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();

        using var adminController = new SystemAdministrationController(runtimeAdministrationRegistry);

        var result = await adminController.ExecuteCallbackFunctionAsync("NonExistentComponent", "NonExistentFunction");
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(objectResult.StatusCode, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task ExecuteCallbackFunction_CallbackFunction_NotFound()
    {
        var nonExistentFunction = "NonExistent";

        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();
        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(ComponentId, CallbackFunctionName, SampleCallbackFunction);

        using var adminController = new SystemAdministrationController(runtimeAdministrationRegistry);

        var result = await adminController.ExecuteCallbackFunctionAsync(ComponentId, nonExistentFunction);
        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(objectResult.StatusCode, StatusCodes.Status404NotFound);
        Assert.False(_callbackFunctionCalled);
    }

    [Fact]
    public async Task ExecuteCallbackFunction_CallbackFunction_Found()
    {
        var runtimeAdministrationRegistry = new RuntimeAdministrationRegistry();
        runtimeAdministrationRegistry.RegisterComponentCallbackFunction(ComponentId, CallbackFunctionName, SampleCallbackFunction);

        using var adminController = new SystemAdministrationController(runtimeAdministrationRegistry);

        var result = await adminController.ExecuteCallbackFunctionAsync(ComponentId, CallbackFunctionName);
        var noContentResult = Assert.IsType<NoContentResult>(result);

        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
        Assert.True(_callbackFunctionCalled);
    }

    private async Task SampleCallbackFunction()
    {
        _callbackFunctionCalled = true;

        await Task.Delay(10);
    }
}
