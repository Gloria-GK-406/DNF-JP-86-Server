using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class LostLandFeverTests
{
    private static readonly CharacterId Character = new(1);
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(150, 1)]
    [InlineData(151, 1)]
    [InlineData(0, 0)]
    [InlineData(200, 0)]
    public void Forest_enemy_tribes_charge_fever_but_allied_and_neutral_teams_do_not(int team, byte expected)
    {
        var actor = new DungeonCellActorState(1, new(69586, 35, 0)
        {
            Declaration = new(69586, DungeonActorSpawnSource.Monster, 0) { MonsterTeam = team },
        }, false, false, [], IsMonster: true);
        Assert.Equal(expected, LostLandFeverRules.Award(new(), actor, At, TimeSpan.FromSeconds(60)).Percent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expiry_command_uses_real_transaction_and_projects_zero_only_to_a_ready_run(bool loading)
    {
        using var fixture = new ShopFixture(services => services.AddSingleton<IDungeonRunDomainService>(provider =>
            global::Dfo.Server.Testing.TestParties.Runs(provider.GetRequiredService<IDomainTransactionAccessor>(),
                parties: provider.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>(),
                participation: provider.GetRequiredService<IDungeonParticipationDomainService>(),
                clock: provider.GetRequiredService<TimeProvider>())));
        var run = TestGameplay.Run(fixture.Services, () =>
        {
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var run = fixture.InTransaction(() => Begin(runs));
            for (ushort actor = 1; actor <= 99; actor++) Death(runs, actor, fixture.Clock.GetUtcNow());
            fixture.Services.GetRequiredService<IDungeonRunProjectionDomainService>().Register(run.RunId, [fixture.Session]);
            if (!loading) runs.TryMarkParticipantReady(Character, run.RunId, run.RoomVersion);
            return run;
        });
        fixture.Clock.Advance(TimeSpan.FromSeconds(60));
        var commands = fixture.Services.GetRequiredService<ICommandBus>();
        InternalCommandOrigin Origin() => new(GameplayCallSource.Timer,
            GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:lost-land-fever-test");
        var due = await commands.SendInternalAsync(new CaptureLostLandFeverExpiriesCommand(), Origin());
        var expiry = Assert.Single(due);
        Assert.Equal(run.RunId, expiry.RunId);
        Assert.True(await commands.SendInternalAsync(new ExpireLostLandFeverCommand(expiry), Origin()));
        Assert.False(await commands.SendInternalAsync(new ExpireLostLandFeverCommand(expiry), Origin()));
        TestGameplay.Run(fixture.Services, () => Assert.Equal(new LostLandFeverState(),
            fixture.Services.GetRequiredService<IDungeonRunDomainService>().Find(Character)!.LostLandFever));
        var notices = fixture.Sender.Messages.OfType<DungeonFeverGaugeMessage>().ToArray();
        if (loading) Assert.Empty(notices);
        else Assert.Equal(0, Assert.Single(notices).Percent);
    }

    [Fact]
    public void Full_charge_discards_overflow_ignores_active_kills_and_expires_once()
    {
        TestGameplay.Run(() =>
        {
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs();
            Begin(runs);
            for (ushort actor = 1; actor <= 98; actor++) Death(runs, actor, At);
            Assert.Equal(98, runs.Find(Character)!.LostLandFever!.Percent);
            var full = Death(runs, 99, At);
            Assert.Equal((byte?)100, full.LostLandFeverPercent);
            Assert.Equal(At.AddSeconds(60), full.Run.LostLandFever!.ActiveUntil);
            Assert.Null(Death(runs, 100, At.AddSeconds(59)).LostLandFeverPercent);
            Assert.Empty(runs.FindExpiredLostLandFevers(At.AddMilliseconds(59999)));
            var expiry = Assert.Single(runs.FindExpiredLostLandFevers(At.AddSeconds(60)));
            Assert.True(runs.StageLostLandFeverExpiry(expiry, At.AddSeconds(60)));
            Assert.Equal(new LostLandFeverState(), runs.Find(Character)!.LostLandFever);
            Assert.False(runs.StageLostLandFeverExpiry(expiry, At.AddSeconds(61)));
            Assert.Equal((byte?)1, Death(runs, 101, At.AddSeconds(61)).LostLandFeverPercent);
        });
    }

    [Fact]
    public void A_kill_after_deadline_starts_new_charge_and_invalidates_the_captured_old_expiry()
    {
        TestGameplay.Run(() =>
        {
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs();
            Begin(runs);
            for (ushort actor = 1; actor <= 99; actor++) Death(runs, actor, At);
            var expiry = Assert.Single(runs.FindExpiredLostLandFevers(At.AddSeconds(60)));
            Assert.Equal((byte?)1, Death(runs, 100, At.AddSeconds(60)).LostLandFeverPercent);
            Assert.False(runs.StageLostLandFeverExpiry(expiry, At.AddSeconds(60)));
            Assert.Equal(1, runs.Find(Character)!.LostLandFever!.Percent);
            runs.End(Character, expiry.RunId);
            Begin(runs);
            Assert.False(runs.StageLostLandFeverExpiry(expiry, At.AddMinutes(2)));
            Assert.Equal(new LostLandFeverState(), runs.Find(Character)!.LostLandFever);
        });
    }

    [Fact]
    public async Task Expiry_changes_only_after_commit_and_a_rollback_keeps_it_due()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var transactions = new DomainTransactionScope();
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs(transactions);
            Begin(runs);
            for (ushort actor = 1; actor <= 99; actor++) Death(runs, actor, At);
            var expiry = Assert.Single(runs.FindExpiredLostLandFevers(At.AddSeconds(60)));
            await using (var tx = transactions.Begin())
            {
                Assert.True(runs.StageLostLandFeverExpiry(expiry, At.AddSeconds(60)));
                Assert.Equal(100, runs.Find(Character)!.LostLandFever!.Percent);
            }
            Assert.Equal(expiry, Assert.Single(runs.FindExpiredLostLandFevers(At.AddSeconds(60))));
            await using (var tx = transactions.Begin())
            {
                Assert.True(runs.StageLostLandFeverExpiry(expiry, At.AddSeconds(60)));
                await tx.CommitAsync(default);
            }
            Assert.Equal(new LostLandFeverState(), runs.Find(Character)!.LostLandFever);
        });
    }

    [Fact]
    public void Non_enemy_and_non_monster_deaths_do_not_charge_and_room_changes_keep_charge()
    {
        TestGameplay.Run(() =>
        {
            IDungeonRunDomainService runs = global::Dfo.Server.Testing.TestParties.Runs();
            var run = Begin(runs);
            Assert.Null(Death(runs, 102, At).LostLandFeverPercent);
            Assert.Null(Death(runs, 103, At).LostLandFeverPercent);
            Assert.Equal((byte?)1, Death(runs, 1, At).LostLandFeverPercent);
            var target = new MazeCell(1, 0);
            var moved = runs.TryMove(Character, run.RunId, run.RoomVersion, target,
                DungeonCellSeed.CompleteEmpty(target, 61001));
            Assert.NotNull(moved);
            Assert.Equal(1, moved.LostLandFever!.Percent);
        });
    }

    private static DungeonRunState Begin(IDungeonRunDomainService runs)
    {
        var layout = new DungeonLayout(4000, 0, 2, 1, new(0, 0), new(1, 0), 61000, 17,
            new Dictionary<MazeCell, DungeonCellDefinition>
            {
                [new(0, 0)] = DungeonCellDefinition.Fixed(61000),
                [new(1, 0)] = DungeonCellDefinition.Fixed(61001),
            }) { Mode = new DungeonRunMode.LostLand(new(600, 1200, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), [], [])) };
        var actors = Enumerable.Range(1, 103).Select(index => new DungeonCellActorState(checked((ushort)index),
            new(3001, 1, index == 99 ? (byte)3 : (byte)0) { IsHostile = index != 102 }, false, false, [],
            IsMonster: index != 103)).ToArray();
        return runs.PreparePartyEntry([new(new(1), Character, 0, true)], layout, 0,
            new(layout.StartCell, 61000, 1, actors, []), DungeonRunId.New(), At).Run;
    }

    private static DungeonActorDeathChange Death(IDungeonRunDomainService runs, ushort actor, DateTimeOffset at) =>
        runs.CommitActorDeathChange(runs.TryClaimActorDeath(Character, actor)! with { OccurredAt = at })!;
}
