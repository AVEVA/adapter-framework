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
using System.IO;
using System.Security;
using Xunit;

namespace AdapterFramework.Data.Framework.Extensions.Tests;

public class ExceptionExtensions_Tests
{
    [Fact]
    public void GetInnerAggregateExceptionMessages_Test()
    {
        var topLevelMessage = "One or more errors occurred.";
        var fistInnerMessage = "Even more errors occured.";
        var secondInnerMessage = "Number isn't in correct format.";
        var anotherInnerMessage = "Unauthorized file access.";
        var lastInnerMessage = "Administrator permissions are required.";

        var aggregateException = new AggregateException(
            topLevelMessage,
            new AggregateException(fistInnerMessage, 
                new FormatException(secondInnerMessage)),
            new IOException(anotherInnerMessage, new SecurityException(lastInnerMessage)));

        var message = aggregateException.GetInnerAggregateExceptionMessages();

        Assert.NotNull(message);
        Assert.DoesNotContain(topLevelMessage, message, StringComparison.InvariantCulture);
        Assert.Contains(fistInnerMessage, message, StringComparison.InvariantCulture);
        Assert.Contains(secondInnerMessage, message, StringComparison.InvariantCulture);
        Assert.Contains(anotherInnerMessage, message, StringComparison.InvariantCulture);
        Assert.Contains(lastInnerMessage, message, StringComparison.InvariantCulture);
        Assert.Contains(aggregateException.GetType().Name, message, StringComparison.InvariantCulture);
    }

    [Fact]
    public void GetExceptionTypeAndMessages_AggregateException_Test()
    {
        var topLevelMessage = "One or more errors occurred.";
        var fistInnerMessage = "Even more errors occured.";
        var secondInnerMessage = "Number isn't in correct format.";
        var anotherInnerMessage = "Unauthorized file access.";
        var lastInnerMessage = "Administrator permissions are required.";

        var aggregateException = new AggregateException(
            topLevelMessage,
            new AggregateException(fistInnerMessage, 
                new FormatException(secondInnerMessage)),
            new IOException(anotherInnerMessage, new SecurityException(lastInnerMessage)));

        var result = aggregateException.GetExceptionTypeAndMessages();

        Assert.NotNull(result);
        Assert.Contains(topLevelMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(fistInnerMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(secondInnerMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(anotherInnerMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(lastInnerMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(aggregateException.GetType().Name, result, StringComparison.InvariantCulture);
    }

    [Fact]
    public void GetExceptionTypeAndMessages_InnerException_Test()
    {
        var exceptionMessage = "Unauthorized file access.";
        var innerExceptionMessage = "Administrator permissions are required.";

        var innerException = new SecurityException(innerExceptionMessage);
        var exceptionWithInnerException = new IOException(exceptionMessage, innerException);

        var result = exceptionWithInnerException.GetExceptionTypeAndMessages();

        Assert.NotNull(result);
        Assert.Contains(exceptionMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(innerExceptionMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(exceptionWithInnerException.GetType().Name, result, StringComparison.InvariantCulture);
        Assert.Contains(innerException.GetType().Name, result, StringComparison.InvariantCulture);
    }

    [Fact]
    public void GetExceptionTypeAndMessages_SimpleException_Test()
    {
        var exceptionMessage = "Unauthorized file access.";

        var exceptionWithInnerException = new IOException(exceptionMessage);

        var result = exceptionWithInnerException.GetExceptionTypeAndMessages();

        Assert.NotNull(result);
        Assert.Contains(exceptionMessage, result, StringComparison.InvariantCulture);
        Assert.Contains(exceptionWithInnerException.GetType().Name, result, StringComparison.InvariantCulture);
    }
}
