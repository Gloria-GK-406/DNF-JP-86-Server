using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Application.Tests.Execution;

/// <summary>
/// Spec RQ-042 on the production composition: the duel shutdown round is a lifecycle service's
/// <c>StoppingAsync</c>, which the host runs before any hosted service stops (the TCP services among them), and it
/// runs as lifecycle commands on the executor.
/// </summary>
/// <remarks>The ordering against a real stop with connected fighters is <c>PvpShutdownHostTests</c> in Host.Tests.</remarks>
[Collection("Scheduler")]
public sealed class PvpShutdownServiceTests
{
    [Fact]
    public async Task The_duel_shutdown_is_a_stopping_hook_not_a_producer_drain()
    {
        await using var host = new TimerSourceHost();

        Assert.IsAssignableFrom<IHostedLifecycleService>(host.Hosted("PvpShutdownService"));
        Assert.DoesNotContain(host.Services.GetServices<IGameplayProducerDrain>(), x => x.GetType().Name.StartsWith("PvpShutdown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_duel_shutdown_round_runs_as_lifecycle_commands_on_the_executor()
    {
        await using var host = new TimerSourceHost();
        var shutdown = Assert.IsAssignableFrom<IHostedLifecycleService>(host.Hosted("PvpShutdownService"));

        await shutdown.StoppingAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));

        var ran = host.Queue.Ran.ToList();
        var cancel = Assert.Single(ran, x => x.CommandType == "lifecycle:PvpShutdown/rooms/cancel");
        // The round's own submissions: the departures still waiting to be projected first, the ended battles last.
        int[] submissions = [.. ran.Select((seen, index) => (seen, index))
            .Where(x => x.seen.CommandType == "lifecycle:PvpShutdown/rooms").Select(x => x.index)];
        Assert.Equal(2, submissions.Length);
        Assert.True(submissions[0] < ran.IndexOf(cancel));
        Assert.True(ran.IndexOf(cancel) < submissions[1]);
        Assert.All([ran[submissions[0]], ran[submissions[1]], cancel], seen =>
        {
            Assert.True(seen.OnExecutor);
            Assert.True(seen.Completed, $"{seen.CommandType} failed on the executor.");
            Assert.Equal(GameplayCallSource.Lifecycle, Assert.NotNull(seen.Current).Source);
        });
        Assert.Empty(host.Services.GetRequiredService<IPvpRoomDomainService>().PendingConclusions());
    }
}
