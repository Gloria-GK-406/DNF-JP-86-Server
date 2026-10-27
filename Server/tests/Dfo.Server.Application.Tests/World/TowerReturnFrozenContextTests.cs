using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Actual_typed_second_member_uncertainty_fences_the_frozen_set_but_preserves_both_actually_declared_reporter_physicals()
    {
        var trace = new TowerReturnSourceTrace { FailureMode = "uncertain" };
        var log = new RecordingExecutionLogger<BossTowerProjectionDriver>();
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
            services.AddSingleton<ILogger<BossTowerProjectionDriver>>(log);
        });
        SelectedSessionIdentity peer = null!;
        NetworkConnectionContext peerPhysical = null!;
        BossTowerSnapshot expected = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel(65);
            await fixture.Seed(65, 2749110, 3);
            peer = await TowerSourceMembers.AddAsync(fixture, 3, 2, 65, 2749110);
            Assert.True(fixture.Sender.Outbound.Connections.TryGet(peer.ConnectionId, out var physical));
            peerPhysical = physical!;
            expected = (await Tower(fixture).EnterAsync([new(fixture.Session, 0), new(peer, 1)], fixture.Session, 42, default)).Run!;
        });
        trace.FaultSession = peer;
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));
        await TowerTestSources.Tick(fixture, true);

        Assert.Equal([fixture.Session, peer], trace.Attempts.Select(attempt => attempt.Session));
        Assert.Equal(DomainTransactionCompletion.Committed, trace.Attempts[0].Transaction.Completion);
        Assert.Equal(DomainTransactionCompletion.Uncertain, trace.Attempts[1].Transaction.Completion);
        Assert.Equal(100007, fixture.Gold);
        Assert.Equal(107, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.All(trace.Attempts, attempt =>
        {
            var command = Assert.IsType<TowerUnitObserved>(attempt.Command);
            Assert.Equal(expected.RunId, command.RunId);
            Assert.Equal(expected.Room.Version, command.RoomVersion);
            Assert.Equal([fixture.Session, peer], command.Sessions);
        });
        Assert.Same(fixture.Connection, trace.Attempts[0].Recipient!.Connection);
        Assert.Same(peerPhysical, trace.Attempts[1].Recipient!.Connection);
        TestGameplay.Run(fixture.Services, () =>
        {
            var selected = fixture.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.True(selected.StandingOf(fixture.Session)!.Fenced);
            Assert.True(selected.StandingOf(peer)!.Fenced);
            var safety = fixture.Services.GetRequiredService<SelectedSessionSafety>();
            Assert.True(safety.IsBlocked(fixture.Connection));
            Assert.True(safety.IsBlocked(peerPhysical));
        });
        Assert.Equal([fixture.Session.ConnectionId, peer.ConnectionId], fixture.Sender.Outbound.Closes);
        Assert.Same(trace.Cause, Assert.IsType<Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException>(
            Assert.Single(trace.ExecutorLog.Entries, entry => entry.Level == LogLevel.Error).Exception).InnerException);
    }

    [Fact]
    public async Task Actual_typed_loading_timeout_removes_dead_member_first_with_the_original_frozen_member_set()
    {
        var trace = new TowerReturnSourceTrace();
        using var fixture = Fixture(configure: services =>
        {
            trace.Install(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
        });
        SelectedSessionIdentity peer = null!;
        BossTowerSnapshot expected = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            fixture.SetLevel(65);
            await fixture.Seed(65, 2749110, 3);
            peer = await AddMember(fixture, 3);
            var towers = Tower(fixture);
            var run = (await towers.EnterAsync([new(fixture.Session, 0), new(peer, 1)], fixture.Session, 42, default)).Run!;
            await towers.LoadAsync(Context(run, fixture.Session), default);
            await towers.LoadAsync(Context(run, peer), default);
            await towers.StartAsync(Context(run, fixture.Session), default);
            await TestEffectFootprints.Run([fixture.Session, peer], cap => towers.DieAsync(Context(run, peer), cap, default));
            await TestEffectFootprints.Run([fixture.Session, peer], cap => towers.KillAsync(Context(run, fixture.Session), run.Room.Actors[0].Uid, cap, default));
            expected = (await towers.NextLevelAsync(Context(run, fixture.Session), 0, 0, default)).Run!;
            Assert.Equal(TowerRoomPhase.Loading, expected.Room.Phase);
            Assert.True(expected.Members.Single(member => member.Session == peer).Dead);
        });
        var before = fixture.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var peerBefore = fixture.Scalar("SELECT exp FROM characters WHERE character_id=3");
        fixture.Clock.Advance(TimeSpan.FromSeconds(121));
        await TowerTestSources.Tick(fixture, true);

        Assert.Equal([peer, fixture.Session], trace.Attempts.Select(attempt => attempt.Session));
        Assert.All(trace.Attempts, attempt =>
        {
            var command = Assert.IsType<TowerUnitObserved>(attempt.Command);
            Assert.Equal(expected.RunId, command.RunId);
            Assert.Equal(expected.Room.Version, command.RoomVersion);
            Assert.Equal(expected.Members.Select(member => member.Session), command.Sessions);
            Assert.Equal(DomainTransactionCompletion.Committed, attempt.Transaction.Completion);
            Assert.False(Assert.IsType<BossTowerActionResult>(attempt.Result).SettlementCreated);
        });
        Assert.NotSame(trace.Attempts[0].Transaction, trace.Attempts[1].Transaction);
        Assert.Equal(before, fixture.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(peerBefore, fixture.Scalar("SELECT exp FROM characters WHERE character_id=3"));
        TestGameplay.Run(fixture.Services, () =>
        {
            Assert.Null(Tower(fixture).Find(fixture.Session));
            Assert.Null(Tower(fixture).Find(peer));
        });
    }
}
