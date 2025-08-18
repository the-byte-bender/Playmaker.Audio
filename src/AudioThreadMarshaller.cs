// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Playmaker.Audio;

public sealed class AudioThreadMarshaller
{
    private readonly ConcurrentQueue<Action> _actionQueue = new();

    /// <summary>
    /// Submits an action to be executed on the audio thread at the beginning of the next update cycle.
    /// This method is thread-safe and returns immediately.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    public void Invoke(Action action)
    {
        _actionQueue.Enqueue(action);
    }

    /// <summary>
    /// Submits a function to be executed on the audio thread and asynchronously waits for its completion.
    /// </summary>
    /// <param name="function">The function to execute. This can be a lambda that captures state.</param>
    /// <returns>A Task that will complete when the function has finished executing on the audio thread.</returns>
    public Task InvokeAsync(Action function)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Action wrapperAction = () =>
        {
            try
            {
                function();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };
        _actionQueue.Enqueue(wrapperAction);

        return tcs.Task;
    }

    /// <summary>
    /// Submits a function with a return value to be executed on the audio thread
    /// and asynchronously waits for the result.
    /// </summary>
    /// <typeparam name="TResult">The return type of the function.</typeparam>
    /// <param name="function">The function to execute.</param>
    /// <returns>A Task that will complete with the result of the function.</returns>
    public Task<TResult> InvokeAsync<TResult>(Func<TResult> function)
    {
        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Action wrapperAction = () =>
        {
            try
            {
                TResult result = function();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        };
        _actionQueue.Enqueue(wrapperAction);

        return tcs.Task;
    }

    internal void ProcessActions()
    {
        while (_actionQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AudioThreadMarshaller] Error executing action: {ex}");
            }
        }
    }
}