using System.Diagnostics;
using Dfo.Server.Abstractions.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Execution;

/// <summary>
/// RQ-009 on a host: drains the producers, then stops the executor. Only <see cref="StoppedAsync"/>
/// acts, because the host calls it after every hosted service's <c>StopAsync</c> (Kestrel, the TCP
/// services, every background service) whatever the registration order.
/// </summary>
/// <remarks>
/// The executor still starts on its first enqueue. When the host's shutdown timeout cancels the stop,
/// the cancellation reaches the host; container disposal then gives the executor its own 10 seconds.
/// </remarks>
internal sealed class GameplayExecutorLifetime(
    GameplayExecutor executor,
    IEnumerable<IGameplayProducerDrain> drains,
    ILogger<GameplayExecutorLifetime> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        foreach (var drain in drains)
        {
            try
            {
                await drain.DrainAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Log(LogLevel.Warning, error, "Draining gameplay producer {Producer} failed; the executor stops anyway",
                    drain.GetType().Name);
            }
        }

        await executor.StopAsync(cancellationToken).ConfigureAwait(false);
        Log(LogLevel.Information, null, "The gameplay executor stopped after draining its producers in {ElapsedMilliseconds} ms",
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    private void Log(LogLevel level, Exception? error, string message, params object?[] args)
    {
        try
        {
            logger.Log(level, error, message, args);
        }
        catch (Exception)
        {
            // Logging never decides the outcome.
        }
    }
}
