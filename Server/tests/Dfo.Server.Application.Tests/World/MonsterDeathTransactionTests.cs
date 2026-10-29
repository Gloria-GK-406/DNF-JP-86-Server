using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Application.Tests.World;

public sealed class MonsterDeathTransactionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task Party_failure_rolls_back_every_recipient_and_actor_then_retry_commits_once(bool duringFlush, bool cycle, bool lostLand)
    {
        using var fixture = new ShopFixture(s =>
        {
            s.AddSingleton<IMonsterKillRewardsDomainService, RewardProbe>();
            s.AddSingleton<IGoldCarryLimitSource, GoldLimit>();
            s.AddSingleton<IExperienceRuleService, FixedProgression>();
            s.AddSingleton<IExperienceCalculationDomainService>(new CommonExperienceEffectTests.Calculation { Earned = 17 });
            var quests = new OrdinaryQuestEffectTests.Rules();
            quests.Quest = quests.Quest with { ObjectiveTypeCode = "clear map", ObjectiveArguments = [61000], InitialTrigger = 1 };
            s.AddSingleton<IQuestContentRuleService>(quests);
            s.AddSingleton<IDungeonRunDomainService>(TestParties.Runs());
        });
        fixture.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'death'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'death',12,1,0); INSERT INTO character_stats(character_id,carried_gold) VALUES(3,100000)");
        fixture.Sql("INSERT INTO character_active_quests VALUES(1,0,2001,1),(3,0,2001,1)");
        var (member, leader, runs, run) = TestGameplay.Run(fixture.Services, () =>
        {
            var registry = fixture.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("death-member"), NetworkEndpointKind.Game, 10011);
            var user = registry.RegisterConnection(connection).OnlineUserId;
            registry.Authenticate(user, new(2));
            var member = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId,
                registry.GetRequiredUser(user).Generation, 10011);
            registry.Select(member);
            registry.EnterWorld(member);
            fixture.Services.GetRequiredService<ISelectedSessionDomainService>().Select(member);
            var leader = fixture.Session;
            fixture.Services.GetRequiredService<ISelectedSessionDomainService>().EnsureInWorld(leader);
            var runs = fixture.Services.GetRequiredService<IDungeonRunDomainService>();
            var layout = new DungeonLayout(10000, 0, 1, 1, new(0, 0), new(0, 0), 61000, 17,
                new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(61000) },
                Purpose: cycle ? new(DungeonRunPurposeKind.Cycle, 2001, DungeonQuestConnectionState.Active) : default)
            {
                Mode = lostLand ? new DungeonRunMode.LostLand(new(600, 1200, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), [], [])) : DungeonRunMode.Ordinary,
            };
            DungeonRunParticipant[] roster = [new(leader.AccountId, leader.CharacterId, 0, true), new(member.AccountId, member.CharacterId, 1, false)];
            TestParties.EnsureEntryParty(runs, roster);
            var run = runs.PreparePartyEntry(roster,
                layout, DungeonDifficulty.Normal,
                new(layout.StartCell, layout.StartRoomMapId, 1, [new(1, new(13099, 1, 0), false, false, [])
                { LostLandBonus = lostLand ? new(System.Collections.Immutable.ImmutableHashSet.Create(13099),
                    [new(10092849,50,DropRarity.Rare,DropItemKind.Stackable),new(10092856,3,DropRarity.Rare,DropItemKind.Stackable)]) : null }], []),
                DungeonRunId.New(), fixture.Clock.GetUtcNow()).Run;
            return (member, leader, runs, run);
        });
        var request = new MonsterDeathSettlementRequest(leader, [leader, member], run.RunId, run.RoomVersion, 1);
        // Real SQLite/domain transaction oracle, distinct from the production inbound wire oracle.
        Task<MonsterDeathSettlementResult> Settle() => TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using var tx=fixture.Services.GetRequiredService<DomainTransactionScope>().Begin();
            var result=await TestEffectFootprints.Run(request.Participants, cap =>fixture.Services.GetRequiredService<IMonsterDeathDomainService>().SettleAsync(request,cap,default));
            await tx.CommitAsync(default);return result;
        });
        var probe = (RewardProbe)fixture.Services.GetRequiredService<IMonsterKillRewardsDomainService>();
        if (duringFlush)
            fixture.Sql("CREATE TRIGGER fail_member BEFORE UPDATE ON character_stats WHEN NEW.character_id=3 BEGIN SELECT RAISE(ABORT,'member flush failure'); END");
        else probe.FailMember = true;

        var failure = await Assert.ThrowsAnyAsync<Exception>(async () => await Settle());
        Assert.True(probe.Transactions.Count == 2, failure.ToString());
        Assert.Equal(0, fixture.Scalar("SELECT SUM(exp) FROM characters"));
        Assert.Equal(100000, fixture.Gold);
        Assert.Equal(100000, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        TestGameplay.Run(fixture.Services, () =>
        {
            Assert.False(runs.Find(leader.CharacterId)!.CurrentCellState.FindActor(1)!.IsDead);
            if (lostLand)
            {
                Assert.Equal(0, runs.Find(leader.CharacterId)!.LostLandFever!.Percent);
                Assert.NotNull(runs.Find(leader.CharacterId)!.CurrentCellState.FindActor(1)!.LostLandBonus);
                Assert.Empty(runs.Find(leader.CharacterId)!.CurrentCellState.GroundItems);
            }
        });
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='dungeon.map-cleared'"));
        Assert.Equal(2, probe.Transactions.Count);
        Assert.Same(probe.Transactions[0], probe.Transactions[1]);
        Assert.NotNull(probe.SqlTransactions[0]);
        Assert.Same(probe.SqlTransactions[0], probe.SqlTransactions[1]);
        Assert.All(probe.Transactions, transaction => Assert.NotNull(transaction));
        Assert.Null(fixture.Services.GetRequiredService<IDomainTransactionAccessor>().Current);

        if (duringFlush) fixture.Sql("DROP TRIGGER fail_member");
        probe.FailMember = false;
        var result = await Settle();
        Assert.NotNull(result.Death?.Change);
        if (lostLand)
        {
            Assert.Equal(50, Assert.Single(result.Death!.Change!.AddedGroundItems, item => item.ItemId == 10092849).Count);
            Assert.Equal(3, Assert.Single(result.Death.Change.AddedGroundItems, item => item.ItemId == 10092856).Count);
        }
        Assert.Equal(2, result.Participants.Count);
        Assert.Equal(100017, fixture.Gold);
        Assert.Equal(100017, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        Assert.Equal(17, fixture.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(17, fixture.Scalar("SELECT exp FROM characters WHERE character_id=3"));
        TestGameplay.Run(fixture.Services, () =>
        {
            Assert.True(runs.Find(leader.CharacterId)!.CurrentCellState.FindActor(1)!.IsDead);
            if (lostLand) Assert.Equal(1, runs.Find(leader.CharacterId)!.LostLandFever!.Percent);
        });
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='dungeon.map-cleared'"));
        Assert.Equal(2, fixture.Scalar("SELECT SUM(trigger_value) FROM character_active_quests"));
        var queued = Assert.Single(await fixture.Services.GetRequiredService<Dfo.Server.Abstractions.DomainEvents.IDomainEventPersistence>()
            .ReadDueAsync("quest.map-clear", fixture.Clock.GetUtcNow(), 64, default));
        var clear = System.Text.Json.JsonSerializer.Deserialize<DungeonMapCleared>(queued.Event.PayloadJson)!;
        Assert.Equal(!lostLand, clear.IsBossRoom);
        await fixture.Services.GetRequiredService<Dfo.Server.Application.DomainEvents.DomainEventDispatcher>().DrainOnceAsync("quest.map-clear");
        Assert.Equal(0, fixture.Scalar("SELECT SUM(trigger_value) FROM character_active_quests"));
        Assert.NotSame(probe.Transactions[0], probe.Transactions[2]);
        Assert.Same(probe.Transactions[2], probe.Transactions[3]);
        Assert.NotSame(probe.SqlTransactions[0], probe.SqlTransactions[2]);
        Assert.Same(probe.SqlTransactions[2], probe.SqlTransactions[3]);
        var duplicate = await Settle();
        Assert.Null(duplicate.Death);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='dungeon.map-cleared'"));
        Assert.Equal(4, probe.Transactions.Count);
        Assert.Equal(100017, fixture.Gold);
        Assert.Equal(100017, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
    }

    private sealed class GoldLimit : IGoldCarryLimitSource
    {
        public int BaseLimitFor(int level) => 1000000;
    }
    private sealed class FixedProgression : IExperienceRuleService
    {
        public DungeonExperienceProgressionPolicy Progression() => new([100, 250, 450, 700], 0, 1, 100);
        public uint WorthOfKill(in KillCircumstances kill) => throw new NotSupportedException();
        public bool IsMonster(byte type) => throw new NotSupportedException();
        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) => throw new NotSupportedException();
        public IReadOnlyList<ExperienceModifier> ModifiersFor(ExperienceEvent occasion,
            IReadOnlyList<AccountPremiumEntryState> premiums, DateTimeOffset at, int channel, int dungeon) => throw new NotSupportedException();
    }

    private sealed class RewardProbe(ICharacterDomainService characters, IDomainTransactionAccessor transactions, IServiceProvider services)
        : IMonsterKillRewardsDomainService
    {
        private readonly MonsterKillRewardsDomainService inner = ActivatorUtilities.CreateInstance<MonsterKillRewardsDomainService>(services);
        public bool FailMember { get; set; }
        public List<IDomainTransaction?> Transactions { get; } = [];
        public List<SqliteTransaction?> SqlTransactions { get; } = [];
        public async ValueTask<MonsterKillEffects> ApplyAsync(SelectedSessionIdentity? session, AccountId accountId,
            CharacterId characterId, IdentifiedKill kill, int dungeonId, DungeonDifficulty difficulty,
            double weight, int partyMemberCount, PartyKey partyKey, bool finalBoss, DungeonRunId? runId,
            RoomActorId? actor, int? worldMapId, IReadOnlyList<RolledDrop>? loot, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken ct)
        {
            Transactions.Add(transactions.Current);
            SqlTransactions.Add(await services.GetRequiredService<SqliteDatabase>().ReadAsync(
                (command, _) => ValueTask.FromResult(command.Transaction), ct));
            var applied = await inner.ApplyAsync(session, accountId, characterId, kill, dungeonId, difficulty,
                weight, partyMemberCount, partyKey, finalBoss, runId, actor, worldMapId, loot, footprint, ct);
            await characters.Get(characterId).AddGoldAsync(17, ct);
            if (FailMember && characterId.Value == 3) throw new InvalidOperationException("member business failure");
            return applied;
        }
    }
}
