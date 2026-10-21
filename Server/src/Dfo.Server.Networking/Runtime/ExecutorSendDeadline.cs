using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Runtime;

/// <summary>
/// Transitional rule 4: until sends leave the executor (T-006), a job that awaits a transport write
/// stops waiting after a deadline, so a client that stops reading cannot freeze the executor and a
/// close callback waiting on the job cannot form a wait cycle with it (RQ-008, plan-review-r2 R-5).
/// Only giving up the wait breaks that cycle; the token alone is left to the transport.
/// </summary>
internal static class ExecutorSendDeadline
{
    /// <summary>
    /// Off the executor <paramref name="send"/> is returned as is. On it, a timeout calls
    /// <paramref name="onTimeout"/> once and throws <see cref="TimeoutException"/>, never a
    /// cancellation, because callers tell a cancellation apart by their own token.
    /// </summary>
    public static Task AwaitAsync(
        Task send, TimeSpan deadline, NetworkConnectionId connectionId, Action onTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(onTimeout);
        return GameplayJob.IsOnExecutor
            ? AwaitOnExecutorAsync(send, deadline, connectionId, onTimeout, cancellationToken)
            : send;
    }

    /// <summary>
    /// Starts <paramref name="close"/> under suppressed flow so it does not inherit the job token or
    /// any other AsyncLocal frame of the caller; failures are logged, never thrown.
    /// </summary>
    public static void StartDetachedClose(Func<Task> close, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(logger);
        // Suppressing an already suppressed flow throws.
        if (ExecutionContext.IsFlowSuppressed())
        {
            _ = Task.Run(() => CloseAsync(close, logger));
            return;
        }
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(() => CloseAsync(close, logger));
    }

    private static async Task AwaitOnExecutorAsync(
        Task send, TimeSpan deadline, NetworkConnectionId connectionId, Action onTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await send.WaitAsync(deadline, cancellationToken).ConfigureAwait(false);
        }
        // The send's own TimeoutException is rethrown as the same object; only ours is a deadline.
        catch (TimeoutException error) when (!ReferenceEquals(error, send.Exception?.InnerException))
        {
            Observe(send);
            onTimeout();
            throw new TimeoutException(
                $"The direct send to {connectionId} did not complete within {deadline} on the gameplay executor.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Observe(send);
            throw;
        }
    }

    private static void Observe(Task abandoned) => abandoned.ContinueWith(
        static task => _ = task.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private static async Task CloseAsync(Func<Task> close, ILogger logger)
    {
        try
        {
            await close().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            try
            {
                logger.LogWarning(error, "Closing a connection after a direct send deadline failed");
            }
            catch (Exception)
            {
                // Nothing is left to report to.
            }
        }
    }
}
