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
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;

namespace AdapterFramework.Data.Framework.DataFlow;

/// <summary>
/// BaseBlock uses an underlying ActionBlock with predefined configurations.
/// </summary>
/// <typeparam name="T">The type of the parameter that the action will be enacted upon.</typeparam>
public abstract class BaseBlock<T> : IDisposable
{
    private const int DisposalDelay = 15 * 1000;
    private const int NotDrained = -1;

    private static readonly Action<ILogger, int, Exception> LogDrainTimeout =
        LoggerMessage.Define<int>(LogLevel.Error, new EventId(DataflowEventIds.DrainTimeout, nameof(LogDrainTimeout)),
            "Dataflow drain timed out after {DrainTimeout} ms; the final flush was skipped and downstream disposal must not rely on it.");
    private static readonly Action<ILogger, Exception> LogDrainFaulted =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.DrainFaulted, nameof(LogDrainFaulted)),
            "The dataflow block faulted before it drained; the final flush was skipped.");
    private static readonly Action<ILogger, Exception> LogWorkerFaulted =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.WorkerFaulted, nameof(LogWorkerFaulted)),
            "The dataflow block faulted and no longer accepts messages.");
    private static readonly Action<ILogger, Exception> LogDrainCanceled =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.DrainCanceled, nameof(LogDrainCanceled)),
            "The dataflow block was canceled before it drained; the final flush was skipped.");
    private static readonly Action<ILogger, Exception> LogFinalFlushFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(DataflowEventIds.FinalFlushFailed, nameof(LogFinalFlushFailed)),
            "The final flush during disposal failed; the batch was not delivered.");

    private readonly Func<T, bool> _postFunc;
    private readonly CancellationToken _token;
    private readonly Task _drainCompletion;
    private int _drainResult = NotDrained;
    private int _drainTimeoutMilliseconds = DisposalDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseBlock{T}"/> class.
    /// </summary>
    /// <param name="logger"><see cref="ILogger"/> instance.</param>
    /// <param name="capacity"> The max number of messages that may be buffered by the block. Use <see cref="DataflowBlockOptions"/> unbounded capacity.</param>
    /// <param name="asyncFlush">Indicate whether the action to invoke with each data element received should be done asynchronously. </param>
    /// <param name="singleProducer">Indicate whether code using the dataFlow block is constrained to one producer at a time.</param>
    /// <param name="token">A cancellation token to observe while waiting for the task to complete.</param>
    protected BaseBlock(ILogger logger, int capacity, bool asyncFlush, bool singleProducer, CancellationToken token)
    {
        Logger = logger;

        _postFunc = capacity == DataflowBlockOptions.Unbounded ? (Func<T, bool>)PostUnbounded : PostBounded;

        var options = new ExecutionDataflowBlockOptions
        {
            BoundedCapacity = capacity,
            CancellationToken = token,
            MaxDegreeOfParallelism = 1,
            SingleProducerConstrained = singleProducer,
        };

        MessageHandler = asyncFlush
            ? new ActionBlock<T>(ProcessAsync, options)
            : new ActionBlock<T>(Process, options);

        _token = token;
        _drainCompletion = MessageHandler.Completion.ContinueWith(
            completion =>
            {
                try
                {
                    OnWorkerStopped();
                }
                catch (Exception exception)
                {
                    Trace.TraceError("Releasing resources after the dataflow block stopped failed. {0}", exception);
                }

                // Report the fault when it happens, not only when the block is disposed.
                if (completion.IsFaulted)
                {
                    SafeLog(LogWorkerFaulted, completion.Exception?.GetBaseException(), "The dataflow block faulted and no longer accepts messages.");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The outcome of waiting for the worker to stop.
    /// </summary>
    protected enum DrainResult
    {
        /// <summary>All accepted messages were processed.</summary>
        Drained,

        /// <summary>The worker was still active when the wait expired.</summary>
        TimedOut,

        /// <summary>The worker faulted.</summary>
        Faulted,

        /// <summary>The worker was canceled.</summary>
        Canceled,
    }

    /// <inheritdoc cref="ActionBlock{TInput}.InputCount"/>
    public int InputCount => MessageHandler.InputCount;

    /// <summary>
    /// Gets or sets the maximum time, in milliseconds, that disposal waits for the worker to drain.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than <see cref="Timeout.Infinite"/> (-1).</exception>
    internal int DrainTimeoutMilliseconds
    {
        get => _drainTimeoutMilliseconds;
        set
        {
            // Validated here so that Dispose, which uses this value, can never throw because of it.
            ArgumentOutOfRangeException.ThrowIfLessThan(value, Timeout.Infinite);
            _drainTimeoutMilliseconds = value;
        }
    }

    /// <summary>
    /// Gets the underlying ActionBlock.
    /// </summary>
    /// <value>
    /// The underlying ActionBlock.
    /// </value>
    protected ActionBlock<T> MessageHandler { get; }

    /// <summary>
    /// Gets the logger.
    /// </summary>
    /// <value>
    /// The <see cref="ILogger"/> instance.
    /// </value>
    protected ILogger Logger { get; }

    /// <summary>
    /// Gets or sets a value indicating whether all resources have been released.
    /// </summary>
    /// <value>
    /// True if object is already disposed. False if it has not been disposed.
    /// </value>
    protected bool Disposed { get; set; }

    /// <inheritdoc cref="ActionBlock{TInput}.Post"/>
    public virtual bool Post(T message) => _postFunc(message);

    /// <summary>
    /// Posts Message to asynchronous action block
    /// </summary>
    /// <param name="message">message of type <typeparamref name="T"/>.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    public virtual Task<bool> PostAsync(T message) => MessageHandler.SendAsync(message, _token);

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        Dispose(true);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Synchronous handling of a message
    /// </summary>
    /// <param name="message">message of type <typeparamref name="T"/>.</param>
    protected abstract void Handle(T message);

    /// <summary>
    /// An abstract method that handles message asynchronously
    /// </summary>
    /// <param name="message">message of type <typeparamref name="T"/>.</param>
    /// <returns>a task.</returns>
    protected abstract Task HandleAsync(T message);

    /// <summary>
    /// Called after the worker has finished handling <paramref name="message"/>, whether or not handling succeeded.
    /// </summary>
    /// <param name="message">The handled message.</param>
    private protected virtual void OnHandled(T message)
    {
    }

    /// <summary>
    /// Called once when the worker has stopped (completed, faulted or canceled). Messages that were accepted
    /// but never handled can be released here.
    /// </summary>
    private protected virtual void OnWorkerStopped()
    {
    }

    /// <summary>
    /// Posts a message to the underlying block without any ownership handling.
    /// </summary>
    /// <param name="message">The message to post.</param>
    /// <returns>True if the message was accepted.</returns>
    private protected bool PostCore(T message) => _postFunc(message);

    /// <summary>
    /// Sends a message to the underlying block asynchronously without any ownership handling.
    /// </summary>
    /// <param name="message">The message to send.</param>
    /// <returns>True if the message was accepted.</returns>
    private protected Task<bool> SendCoreAsync(T message) => MessageHandler.SendAsync(message, _token);

    /// <summary>
    /// Dispose and guards against multiple dispose calls. Never throws because of a failed drain.
    /// </summary>
    /// <param name="disposing">true if called by user. False if called by finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Disposed)
        {
            return;
        }

        try
        {
            if (disposing)
            {
                CompleteAndWait();
            }
        }
        finally
        {
            Disposed = true;
        }
    }

    /// <summary>
    /// Stops admission and waits up to the configured drain timeout for handlers to finish.
    /// </summary>
    /// <returns>The drain outcome. Only <see cref="DrainResult.Drained"/> certifies that the worker has stopped cleanly.</returns>
    protected DrainResult CompleteAndWait() => CompleteAndWait(DrainTimeoutMilliseconds);

    /// <summary>
    /// Stops admission and waits for handlers to finish before caller-thread flushing or downstream disposal.
    /// Never throws for a valid timeout; failures are logged once and reported through the result.
    /// </summary>
    /// <param name="millisecondsTimeout">Maximum drain time in milliseconds, or <see cref="Timeout.Infinite"/> to wait indefinitely.</param>
    /// <returns>The drain outcome. Only <see cref="DrainResult.Drained"/> certifies that the worker has stopped cleanly.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is less than <see cref="Timeout.Infinite"/>. Checked before any state changes.</exception>
    /// <remarks>
    /// The outcome is remembered. After a timeout, later calls return <see cref="DrainResult.TimedOut"/> immediately
    /// while the worker is still running, so a drain is never waited for or reported twice.
    /// </remarks>
    protected DrainResult CompleteAndWait(int millisecondsTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, Timeout.Infinite);

        var known = Volatile.Read(ref _drainResult);
        if (known != NotDrained && ((DrainResult)known != DrainResult.TimedOut || !_drainCompletion.IsCompleted))
        {
            return (DrainResult)known;
        }

        MessageHandler.Complete();

        // The continuation never faults or cancels, so Wait only reports whether it finished in time.
        if (!_drainCompletion.Wait(millisecondsTimeout))
        {
            SafeLog(LogDrainTimeout, millisecondsTimeout, "Dataflow drain timed out after {0} ms; the final flush was skipped.");
            return SetDrainResult(DrainResult.TimedOut);
        }

        var completion = MessageHandler.Completion;
        if (completion.IsCanceled)
        {
            SafeLog(LogDrainCanceled, null, "The dataflow block was canceled before it drained; the final flush was skipped.");
            return SetDrainResult(DrainResult.Canceled);
        }

        if (completion.IsFaulted)
        {
            // The exception itself was already logged when the worker faulted.
            SafeLog(LogDrainFaulted, null, "The dataflow block faulted before it drained; the final flush was skipped.");
            return SetDrainResult(DrainResult.Faulted);
        }

        return SetDrainResult(DrainResult.Drained);
    }

    /// <summary>
    /// Waits for the worker to stop and, only if it drained cleanly, runs <paramref name="flush"/> on the caller thread.
    /// Never throws; a failing flush is logged.
    /// </summary>
    /// <param name="flush">The final flush.</param>
    protected void DrainAndFlush(Action flush)
    {
        ArgumentNullException.ThrowIfNull(flush);

        try
        {
            if (CompleteAndWait() == DrainResult.Drained)
            {
                flush();
            }
        }
        catch (Exception exception)
        {
            SafeLog(LogFinalFlushFailed, exception, "The final flush during disposal failed; the batch was not delivered.");
        }
    }

    private DrainResult SetDrainResult(DrainResult result)
    {
        Volatile.Write(ref _drainResult, (int)result);
        return result;
    }

    private void SafeLog(Action<ILogger, Exception> log, Exception exception, string fallback)
    {
        try
        {
            if (Logger != null)
            {
                log(Logger, exception);
            }
            else
            {
                Trace.TraceError("{0} {1}", fallback, exception);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceError("Logging failed. {0} {1}", fallback, loggingException);
        }
    }

    private void SafeLog(Action<ILogger, int, Exception> log, int value, string fallbackFormat)
    {
        try
        {
            if (Logger != null)
            {
                log(Logger, value, null);
            }
            else
            {
                Trace.TraceError(fallbackFormat, value);
            }
        }
        catch (Exception loggingException)
        {
            Trace.TraceError("Logging failed. {0}", loggingException);
        }
    }

    private void Process(T message)
    {
        try
        {
            Handle(message);
        }
        finally
        {
            OnHandled(message);
        }
    }

    private async Task ProcessAsync(T message)
    {
        try
        {
            await HandleAsync(message).ConfigureAwait(false);
        }
        finally
        {
            OnHandled(message);
        }
    }

    private bool PostBounded(T message) => MessageHandler.SendAsync(message, _token).GetAwaiter().GetResult();

    private bool PostUnbounded(T message) => MessageHandler.Post(message);
}
