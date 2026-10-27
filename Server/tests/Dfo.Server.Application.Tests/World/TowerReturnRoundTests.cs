using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData("overlap")]
    [InlineData("arrival")]
    [InlineData("drain")]
    [InlineData("admission-cancel")] [InlineData("refusal")] [InlineData("hosted")]
    public async Task Real_typed_rounds_keep_claim_identity_late_arrivals_and_the_complete_producer_drain(string mode)
    {
        var gate = new TowerRoundGate();
        using var producer = new CancellationTokenSource();
        gate.CancelCause = new OperationCanceledException("handler construction cancelled before claim", producer.Token);
        using var fixture = Fixture(configure: services =>
        {
            services.AddSingleton<CommandBus>();
            services.AddSingleton<Dfo.Server.Application.Transactions.CommandWorkUnits>();
            services.AddScoped<ICommandWorkUnits>(p =>
            {
                if (gate.FailHandler) { gate.FailHandler = false; throw gate.CancelCause; }
                return p.GetRequiredService<Dfo.Server.Application.Transactions.CommandWorkUnits>();
            });
            services.AddSingleton<ICommandBus>(p => new TowerRoundGateBus(p.GetRequiredService<CommandBus>(), gate));
            services.AddSingleton<IGameplayQueue>(p => new RecordingGameplayQueue(new GameplayQueue(p.GetRequiredService<GameplayShardSelector>())));
        });
        SelectedSessionIdentity first = null!, second = null!;
        BossTowerActionResult later = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel(65); await fixture.Seed(65, 2749110, 3);
            first = await TowerSourceMembers.AddAsync(fixture, 3, 2, 65, 2749110);
            var third = await TowerSourceMembers.AddAsync(fixture, 4, 3, 65, 2749110);
            second = await TowerSourceMembers.AddAsync(fixture, 5, 4, 65, 2749110);
            foreach (var cohort in new[] { new[] { fixture.Session, first }, new[] { third, second } })
            {
                var run = (await Tower(fixture).EnterAsync([new(cohort[0], 0), new(cohort[1], 1)], cohort[0], 42, default)).Run!;
                await Tower(fixture).LoadAsync(Context(run, cohort[0]), default);
                await Tower(fixture).LoadAsync(Context(run, cohort[1]), default);
                var action = await TestEffectFootprints.Run(cohort, cap => Tower(fixture).LeaveAsync(Context(run, cohort[0]), false, cap, default));
                if (cohort[1] == first) fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().Enqueue(action);
                else later = action;
            }
        });
        var source = fixture.Services.GetServices<IHostedService>().OfType<BossTowerReturnSweepService>().Single();
        var firstRound = mode == "hosted" ? source.StartAsync(default) : source.TickAsync(producer.Token);
        await gate.Captured.Task.WaitAsync(ExecutionTest.Patience);
        Task? drain = null;
        try
        {
            Assert.Empty(fixture.Sender.Outbound.MessagesFor(first.ConnectionId));
            if (mode == "hosted")
            {
                await Task.Delay(650);
                var recording = (RecordingGameplayQueue)fixture.Services.GetRequiredService<IGameplayQueue>();
                Assert.Equal(1, recording.EnqueuedCount("timer:BossTowerReturnSweepService"));
            }
            if (mode == "admission-cancel") { producer.Cancel(); gate.FailHandler = true; }
            if (mode == "refusal") await fixture.Services.GetRequiredService<GameplayExecutor>().StopAsync(default);
            if (mode == "overlap")
            {
                await source.TickAsync(default).WaitAsync(ExecutionTest.Patience);
                Assert.Single(fixture.Sender.Outbound.MessagesFor(first.ConnectionId), message => message.MessageType == ServerMessageType.GameUdpHost);
            }
            if (mode == "arrival")
                TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().Enqueue(later));
            if (mode == "drain")
            {
                drain = source.DrainAsync(default).AsTask();
                Assert.False(drain.IsCompleted);
                var refusal = await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => source.TickAsync(default));
                Assert.False(GameplayJob.EndedAJob(refusal));
            }
        }
        finally { gate.Release.TrySetResult(); }
        if (mode == "refusal")
        {
            var refusal = await Assert.ThrowsAsync<GameplayExecutorStoppedException>(() => firstRound.WaitAsync(ExecutionTest.Patience));
            Assert.False(GameplayJob.EndedAJob(refusal));
            // Read-only terminal inspection: the production executor is stopped; no claim ran.
            Assert.Single(TestGameplay.Run(() => fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().SnapshotPending()));
            Assert.Empty(fixture.Sender.Outbound.MessagesFor(first.ConnectionId));
            return;
        }
        await firstRound.WaitAsync(ExecutionTest.Patience);
        if (mode == "admission-cancel")
        {
            Assert.Single(TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().SnapshotPending()));
            Assert.Empty(fixture.Sender.Outbound.MessagesFor(first.ConnectionId));
            await source.TickAsync(default).WaitAsync(ExecutionTest.Patience);
        }
        if (mode == "hosted")
        {
            var recording = (RecordingGameplayQueue)fixture.Services.GetRequiredService<IGameplayQueue>();
            Assert.True(await recording.WaitForAsync(job => job.CommandType == "timer:BossTowerReturnSweepService/due", ExecutionTest.Patience));
            await source.StopAsync(default);
        }
        if (drain is not null) await drain.WaitAsync(ExecutionTest.Patience);
        Assert.Single(fixture.Sender.Outbound.MessagesFor(first.ConnectionId), message => message.MessageType == ServerMessageType.GameUdpHost);
        Assert.Empty(fixture.Sender.Outbound.MessagesFor(second.ConnectionId));
        if (mode == "arrival")
        {
            var pending = TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>().SnapshotPending());
            Assert.Single(pending);
            await source.TickAsync(default).WaitAsync(ExecutionTest.Patience);
            Assert.Single(fixture.Sender.Outbound.MessagesFor(second.ConnectionId), message => message.MessageType == ServerMessageType.GameUdpHost);
        }
        var queue = (RecordingGameplayQueue)fixture.Services.GetRequiredService<IGameplayQueue>();
        var actual = queue.Ran.Where(job => job.CommandType.StartsWith("timer:BossTowerReturnSweepService", StringComparison.Ordinal)).ToArray();
        Assert.All(actual, job =>
        {
            Assert.True(job.OnExecutor);
            Assert.True(job.Completed || mode == "admission-cancel" && job.CommandType.EndsWith("/departure", StringComparison.Ordinal));
            Assert.Equal(GameplayCallSource.Timer, job.Current!.Value.Source);
            Assert.Null(job.Current.Value.SourceConnection);
            Assert.All(job.Current.Value.Ancestors, hop => Assert.DoesNotContain("unknown", hop.CommandType));
            Assert.Equal(job.CommandType.EndsWith("/departure", StringComparison.Ordinal) || job.CommandType.EndsWith("/due", StringComparison.Ordinal) ? 1 : 0, job.Current.Value.Depth);
        });
        Assert.Same(source, fixture.Services.GetServices<IGameplayProducerDrain>().Single(p => ReferenceEquals(p, source)));
        Assert.Same(fixture.Services.GetRequiredService<BossTowerDepartureProjectionQueue>(), fixture.Services.GetRequiredService<IBossTowerDepartureProjectionDomainService>());
    }
}
internal sealed class TowerRoundGate
{
    internal int Taken;
    internal bool FailHandler;
    internal OperationCanceledException CancelCause = null!;
    internal TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
internal sealed class TowerRoundGateBus(ICommandBus actual, TowerRoundGate gate) : ICommandBus
{
    public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken token = default) where T : notnull => actual.SendAsync(command, token);
    public async ValueTask<T> SendInternalAsync<T>(ICommand<T> command, InternalCommandOrigin origin, CancellationToken token = default) where T : notnull
    {
        var result = await actual.SendInternalAsync(command, origin, token);
        if (command is CaptureBossTowerDeparturesCommand && Interlocked.CompareExchange(ref gate.Taken, 1, 0) == 0)
        { gate.Captured.TrySetResult(); await gate.Release.Task; }
        return result;
    }
}
