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
using System.Text;

namespace AdapterFramework.Data.Framework.Extensions;

public static class ExceptionExtensions
{
    /// <summary>
    /// Returns inner exception messages with type information from <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="aggregateException">Exception to get messages from.</param>
    /// <returns>Inner exception type and message combinations for exceptions wrapped in the exception./></returns>
    public static string GetInnerAggregateExceptionMessages(this AggregateException aggregateException)
    {
        ThrowHelper.ThrowIfArgumentNull(aggregateException, nameof(aggregateException));

        var exceptionMessages = new StringBuilder().Append("Inner Exceptions:");
        for (int i = 0; i < aggregateException.InnerExceptions.Count; ++i)
        {
            exceptionMessages.AppendLine().Append(i + 1 + ". Exception Type: " + aggregateException.InnerExceptions[i].GetType().Name 
                                             + " Message: " + aggregateException.InnerExceptions[i].Message);

            var innerException = aggregateException.InnerExceptions[i].InnerException;

            if (!string.IsNullOrWhiteSpace(innerException?.Message))
            {
                exceptionMessages.Append(" Inner Exception Message: " + innerException.Message);
            }
        }

        return exceptionMessages.ToString();
    }

    /// <summary>
    /// Returns exception type and messages including type and message combinations for inner exceptions in case the exception type is <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="exception">Exception to get formatted output from.</param>
    /// <returns>Formatted exception output.</returns>
    public static string GetExceptionTypeAndMessages(this Exception exception)
    {
        ThrowHelper.ThrowIfArgumentNull(exception, nameof(exception));

        var exceptionOutputBuilder = new StringBuilder();
        exceptionOutputBuilder.Append("Exception type: " + exception.GetType().Name + ". Message: " + exception.Message);

        if (exception is AggregateException aggregateException)
        {
            exceptionOutputBuilder.Append(" " + aggregateException.GetInnerAggregateExceptionMessages());

            return exceptionOutputBuilder.ToString();
        }

        if (exception.InnerException != null)
        {
            if (!string.IsNullOrWhiteSpace(exception.InnerException.Message))
            {
                exceptionOutputBuilder.Append(" Inner Exception: ")
                    .Append(exception.InnerException.GetType().Name)
                    .Append(" Message: " + exception.InnerException.Message);
            }
        }

        return exceptionOutputBuilder.ToString();
    }
}
