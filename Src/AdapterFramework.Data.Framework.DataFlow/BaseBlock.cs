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

    private readonly Func<T, bool> _postFunc;
    private readonly CancellationToken _token;

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
            ? new ActionBlock<T>(HandleAsync, options)
            : new ActionBlock<T>(Handle, options);

        _token = token;
    }

    /// <inheritdoc cref="ActionBlock{TInput}.InputCount"/>
    public int InputCount => MessageHandler.InputCount;

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
    public bool Post(T message) => _postFunc(message);

    /// <summary>
    /// Posts Message to asynchronous action block
    /// </summary>
    /// <param name="message">message of type <typeparamref name="T"/>.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    public Task<bool> PostAsync(T message) => MessageHandler.SendAsync(message, _token);

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
    /// Dispose and guards against multiple dispose calls.
    /// </summary>
    /// <param name="disposing">true if called by user. False if called by finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Disposed)
        {
            return;
        }

        if (disposing)
        {
            MessageHandler.Complete();
            SpinWait.SpinUntil(() => MessageHandler.Completion.IsCompleted, DisposalDelay);
        }

        Disposed = true;
    }

    private bool PostBounded(T message) => MessageHandler.SendAsync(message, _token).GetAwaiter().GetResult();

    private bool PostUnbounded(T message) => MessageHandler.Post(message);
}
