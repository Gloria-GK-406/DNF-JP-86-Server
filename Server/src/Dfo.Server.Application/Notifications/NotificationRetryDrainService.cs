using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Notifications;

/// <summary>The one loop this service is allowed to have.</summary>
/// <remarks>
/// A notification is published because something happened, never because a clock said so -- that
/// is a separate mechanism and not this one. This loop is not an exception to that: it drives
/// nothing of its own, it only revisits recipients an earlier publish could not reach. Without it
/// the retry limit a notification declares would describe attempts nobody ever makes.
/// <para>
/// A pass that throws is logged and the loop carries on. The alternative is a server that stops
/// retrying for the rest of its life over one bad pass, and never says so.
/// </para>
/// </remarks>
internal sealed class NotificationRetryDrainService(
    INotificationRetryDrain drain,
    NotificationDeliveryOptions options,
    IGameplayQueue queue,
    ILogger<NotificationRetryDrainService> logger) : IHostedService, IAsyncDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly CoalescingGameplaySource passes = new(queue);
    private PeriodicTimer? interval;
    private Task draining = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Built here rather than inside the loop: a misconfigured interval throws from the host's
        // own start, where somebody sees it. Inside an async method it would land in a task
        // nothing awaits until shutdown, and a server that silently never retries is exactly the
        // failure this outcome is worst at noticing.
        interval = new PeriodicTimer(options.RetryInterval);
        draining = DrainUntilStoppedAsync(interval, stopping.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        await draining.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        stopping.Dispose();
        interval?.Dispose();
    }

    private async Task DrainUntilStoppedAsync(PeriodicTimer interval, CancellationToken cancellationToken)
    {
        using (interval)
        try
        {
            while (await interval.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await DrainOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // One pass is one timer CMD on the executor; the catch stays here, where it waits.
            await GameplayTimerWork.TickAsync(passes, "timer:NotificationRetryDrainService",
                drain.DrainAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(error, "A notification retry pass failed.");
        }
    }
}
