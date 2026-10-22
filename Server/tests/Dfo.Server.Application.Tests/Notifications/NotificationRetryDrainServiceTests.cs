using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// The one loop this service is allowed to have, and the three things it has to get right.
/// </summary>
/// <remarks>
/// Without it the retry limit a notification declares would describe attempts nobody ever makes,
/// and nothing else here would notice: every other case in this project drains by hand.
/// <para>
/// What is asserted is that it comes round again, that it stops, and that one bad pass does not
/// end it. Not how often -- a case that asserted a period would be asserting the machine's mood.
/// </para>
/// </remarks>
public sealed class NotificationRetryDrainServiceTests
{
    [Fact]
    public async Task The_loop_comes_round_again_rather_than_draining_once()
    {
        var drain = new CountingDrain();
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        await using var service = Service(drain, executor);

        await service.StartAsync(CancellationToken.None);
        await drain.ReachedAsync(passes: 2);

        Assert.True(drain.Passes >= 2);
    }

    /// <summary>Stopping the host stops the loop, and waits for the pass in flight.</summary>
    [Fact]
    public async Task Stopping_ends_the_loop()
    {
        var drain = new CountingDrain();
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        await using var service = Service(drain, executor);
        await service.StartAsync(CancellationToken.None);
        await drain.ReachedAsync(passes: 1);

        await service.StopAsync(CancellationToken.None);
        var afterStopping = drain.Passes;
        await Task.Delay(50);

        Assert.Equal(afterStopping, drain.Passes);
    }

    /// <summary>A pass that throws is not the end of retrying for the life of the process.</summary>
    /// <remarks>
    /// The alternative is a server that quietly stops retrying after one bad pass and never says
    /// so, which is the failure this whole outcome is least able to notice.
    /// </remarks>
    [Fact]
    public async Task A_pass_that_throws_does_not_end_the_loop()
    {
        var drain = new CountingDrain { ThrowOnPass = 1 };
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        await using var service = Service(drain, executor);

        await service.StartAsync(CancellationToken.None);
        await drain.ReachedAsync(passes: 3);

        Assert.True(drain.Passes >= 3);
    }

    /// <summary>An interval that cannot tick is refused where somebody sees it.</summary>
    /// <remarks>
    /// The timer is built in <c>StartAsync</c> rather than inside the loop for this reason alone:
    /// inside, the throw lands in a task nothing awaits until shutdown, and the server runs on
    /// having quietly stopped retrying. This is the case that keeps it where it is.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task An_interval_that_cannot_tick_is_refused_at_startup(int milliseconds)
    {
        await using var executor = new GameplayExecutor(NullLogger<GameplayExecutor>.Instance);
        await using var service = Service(
            new CountingDrain(), executor, TimeSpan.FromMilliseconds(milliseconds));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.StartAsync(CancellationToken.None));
    }

    private static NotificationRetryDrainService Service(
        CountingDrain drain, GameplayExecutor executor, TimeSpan? interval = null) =>
        new(drain,
            new NotificationDeliveryOptions(
                PerRecipientTimeout: TimeSpan.FromSeconds(1),
                RetryInterval: interval ?? TimeSpan.FromMilliseconds(5)),
            new GameplayQueue(new GameplayShardSelector(executor)),
            NullLogger<NotificationRetryDrainService>.Instance);

    private sealed class CountingDrain : INotificationRetryDrain
    {
        private readonly TaskCompletionSource reached = new();
        private int wanted = int.MaxValue;

        public int Passes { get; private set; }

        public int ThrowOnPass { get; init; }

        /// <summary>Waits for the loop rather than for a clock, so a slow machine is only slow.</summary>
        public async Task ReachedAsync(int passes)
        {
            wanted = passes;
            if (Passes >= passes)
                return;

            await reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public ValueTask DrainAsync(CancellationToken cancellationToken)
        {
            Passes++;
            if (Passes >= wanted)
                reached.TrySetResult();
            return Passes == ThrowOnPass
                ? ValueTask.FromException(new InvalidOperationException("this pass went wrong"))
                : ValueTask.CompletedTask;
        }
    }
}
