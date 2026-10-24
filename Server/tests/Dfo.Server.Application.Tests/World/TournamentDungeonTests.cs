using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class TournamentDungeonTests
{
    private static DungeonEntryContext Entry() => new(120,
        DungeonEntryFacts.None with { Difficulty = (DungeonDifficulty)2 }, 0, 0,
        [new(new(1), new(71), 0, true)]);

    private static async Task<LearningFixture> Fixture(int tickets = 3, Action<IServiceCollection>? configure = null)
    {
        var f = new LearningFixture(useArchive: true, configure: s =>
        {
            s.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
            configure?.Invoke(s);
        });
        await f.SeedAsync();
        f.Sql("INSERT OR IGNORE INTO character_stats(character_id) VALUES(71);");
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new(71),
            new PersistentItemContainer(ContainerId.ForCharacter(new(71), InventoryContainerType.Main),
                new(InventoryContainerType.Main, 0), f.Services.GetRequiredService<IItemDomainStore>(), f.Scope,
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        await using var tx = f.Scope.Begin();
        if (tickets > 0)
            await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 4726, tickets, default);
        await tx.CommitAsync(default);
        return f;
    }

    private static int Count(LearningFixture f, int item) => f.Scalar($"SELECT COALESCE(SUM(s.count),0) FROM item_slots s JOIN items i ON s.core_uid=i.item_uid WHERE s.owner_scope='character' AND s.owner_id=71 AND substr(hex(i.item_core),3,8)='{Convert.ToHexString(BitConverter.GetBytes(item))}'");
    private static int Gold(LearningFixture f) => f.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=71");
    private static ITournamentDungeonDomainService Tournament(LearningFixture f) => f.Services.GetRequiredService<ITournamentDungeonDomainService>();
    private static IDungeonRunDomainService Runs(LearningFixture f) => f.Services.GetRequiredService<IDungeonRunDomainService>();
    private static TournamentDungeonState State(LearningFixture f) => Assert.IsType<TournamentDungeonState>(Tournament(f).Find(new(71)));

    private static async Task<MonsterDeathSettlementResult> Kill(LearningFixture f, ushort actor, bool commit = true, SelectedSessionIdentity? session = null)
    {
        var run = Runs(f).Find(new CharacterId(71))!;
        await using var tx = f.Scope.Begin();
        using var footprint = new TestEffectFootprints.Lease(session ?? f.Session);
        var context = new CommandExecutionContext(Guid.NewGuid(), typeof(ProcessMonsterDeathCommand), typeof(ProcessMonsterDeathCommandHandler), CommandExecutionKind.Transactional);
        context.SupplyLegacyFootprint(footprint);
        var result = await ActivatorUtilities.CreateInstance<ProcessMonsterDeathCommandHandler>(f.Services)
            .HandleAsync(context, new(new(session ?? f.Session, [session ?? f.Session], run.RunId, run.RoomVersion, actor)), default);
        if (commit) await tx.CommitAsync(default);
        return result;
    }

    private static async Task Ready(LearningFixture f)
    {
        var entry = await Enter(f);
        Assert.True(entry.Entry is not null, $"Entry refused: {entry.Failure}; {entry.Detail}");
        var run = Runs(f).Find(new CharacterId(71))!;
        Runs(f).TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
        Assert.Contains(new CharacterId(71), Runs(f).Find(new CharacterId(71))!.ReadyParticipants);
    }

    private static async Task<TournamentActionResult> Champion(LearningFixture f)
    {
        await Ready(f);
        TournamentActionResult? result = null;
        for (ushort actor = 1; actor <= 4; actor++)
        {
            var death = await Kill(f, actor);
            Assert.NotNull(death.Death?.Change);
            result = Assert.IsType<TournamentActionResult>(death.Tournament);
            Assert.Equal(actor, State(f).CompletedRounds);
            Assert.Equal(actor == 4, State(f).IsFinished);
        }
        return result!;
    }

    private static async Task<TournamentActionResult?> Select(LearningFixture f, byte row, byte card)
    {
        await using var tx = f.Scope.Begin();
        var result = await Tournament(f).SelectRewardAsync(f.Session, State(f).RunId, row, card, default);
        await tx.CommitAsync(default);
        return result;
    }

    private static async Task<DungeonEntryAttempt> Enter(LearningFixture f, DungeonEntryContext? context = null, bool commit = true)
    {
        await using var tx = f.Scope.Begin();
        var result = await ActivatorUtilities.CreateInstance<EnterDungeonCommandHandler>(f.Services)
            .HandleAsync(new(Guid.NewGuid(), typeof(EnterDungeonCommand), typeof(EnterDungeonCommandHandler), CommandExecutionKind.Transactional),
                new(context ?? Entry()), default);
        if (commit) await tx.CommitAsync(default);
        return result;
    }

    [SkippableFact]
    public async Task Yellow_dragon_enters_one_arena_with_four_frozen_opponents_and_only_round_fatigue()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var result = await Enter(f);
            Assert.True(result.Entry is not null, $"Entry refused: {result.Failure}; {result.Detail}");
            Assert.Equal(17100, result.Entry.Layout.StartRoomMapId);
            Assert.Equal(4, result.Entry.Visits[new(71)].Run.CurrentCellState.Actors.Count);
            Assert.Equal(2, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        });
    }

    [SkippableFact]
    public async Task Four_ordered_death_commands_finish_champion_once_without_ordinary_clear()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var result = await Champion(f);
            var state = State(f);
            Assert.Equal(TournamentStatus.Champion, state.Status);
            Assert.NotNull(result.Experience);
            Assert.Equal(200000u, result.Experience.Breakdown.Base);
            Assert.Equal(8, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
            Assert.Equal(2, Count(f, 4726));
            Assert.Equal(2, state.Rewards.Count);
            Assert.All(state.Rewards, row => Assert.Equal(2, row.Count));
            var run = Runs(f).Find(new CharacterId(71))!;
            Assert.True(run.ClearOpen);
            Assert.All(run.CurrentCellState.Actors, actor => Assert.True(actor.IsDead));
            Assert.Null(await f.Services.GetRequiredService<IDungeonClearDomainService>()
                .EvaluateAsync(new(1), new(71), run.RunId, run.RoomVersion, default));
            var gold = Gold(f);
            var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=71");
            var duplicate = await Kill(f, 4);
            Assert.Null(duplicate.Death);
            Assert.Null(duplicate.Tournament);
            Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
            Assert.Equal(gold, Gold(f));
        });
    }

    [SkippableFact]
    public async Task Blue_dragon_routes_to_tournament_consumes_its_ticket_and_finishes_four_rounds()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71), 4727, 1, default);
                await tx.CommitAsync(default);
            }
            var entry = await Enter(f, Entry() with { DungeonId = 121 });
            Assert.True(entry.Entry is not null, $"Entry refused: {entry.Failure}; {entry.Detail}");
            Assert.Equal(17101, entry.Entry.Layout.StartRoomMapId);
            Assert.Equal(0, Count(f, 4727));
            Assert.Equal(3, Count(f, 4726));
            var run = Runs(f).Find(new CharacterId(71))!;
            Runs(f).TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            TournamentActionResult? result = null;
            for (ushort actor = 1; actor <= 4; actor++)
            {
                var death = await Kill(f, actor);
                Assert.NotNull(death.Death?.Change);
                result = Assert.IsType<TournamentActionResult>(death.Tournament);
            }
            Assert.Equal(TournamentStatus.Champion, State(f).Status);
            Assert.Equal(500000u, result!.Experience!.Breakdown.Base);
            Assert.Equal(8, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
            Assert.All(State(f).Rewards[1], card => Assert.Equal(3323, card.ItemId));
        });
    }

    [SkippableTheory]
    [InlineData(true,0,4)]
    [InlineData(true,29,30)]
    [InlineData(false,0,0)]
    public async Task Black_dragon_enters_at_zero_fatigue_and_its_apc_deaths_grant_only_active_quest_breath_up_to_target(bool hasQuest, int held, int expected)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available,ContentArchive.SkipReason);
            await using var f = await Fixture(tickets:0);
            if (hasQuest) f.Sql("INSERT INTO character_active_quests VALUES(71,0,4730,30)");
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<ICharacterFatigueDomainService>().SpendAsync(new(71),ushort.MaxValue,default);
                if (held > 0) await f.Services.GetRequiredService<IItemGrantDomainService>().GrantAllPersistentAsync(new(71),4831,held,default);
                await tx.CommitAsync(default);
            }
            var before = await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71),default);
            Assert.Equal(0,before.Remaining);
            var entry = await Enter(f,Entry() with { DungeonId=122 });
            Assert.True(entry.Entry is not null,$"Entry refused: {entry.Failure}; {entry.Detail}");
            var run = Runs(f).Find(new CharacterId(71))!;
            Assert.Equal(17102,run.CurrentRoomMapId); Assert.Equal(DungeonDifficulty.Normal,run.Difficulty);
            Assert.All(run.CurrentCellState.Actors,actor =>
            {
                Assert.True(actor.IsAiCharacter); Assert.False(actor.IsMonster);
                if (hasQuest) Assert.Contains(actor.PendingLoot,x => x.ItemId==4831 && x.Delivery==RolledDropDelivery.DirectQuestItem);
                else Assert.Empty(actor.PendingLoot);
            });
            Runs(f).TryMarkParticipantReady(new(71),run.RunId,run.RoomVersion);
            for (ushort actor=1; actor<=4; actor++)
            {
                Assert.NotNull((await Kill(f,actor)).Death);
                var count = Count(f,4831);
                Assert.Null((await Kill(f,actor)).Death);
                Assert.Equal(count,Count(f,4831));
            }
            Assert.Equal(expected,Count(f,4831));
            Assert.Equal(TournamentStatus.Champion,State(f).Status);
            Assert.All(State(f).Rewards[1],card => Assert.Equal(4831,card.ItemId));
            Assert.Equal(before,await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71),default));
            if (hasQuest) Assert.Equal(expected==30 ? 0 : 30-expected,f.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=71 AND quest_id=4730"));
        });
    }

    [SkippableTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Black_dragon_wire_player_ids_do_not_collide_and_all_four_nonordinal_actor_deaths_finish(ushort wireId)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture(tickets: 0);
            var registry = f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>();
            Assert.True(registry.TryGetByConnection(f.Session.ConnectionId, out var online));
            var session = f.Session with { WireUserId = wireId, ConnectionGeneration = online!.Generation };
            registry.Select(session);
            registry.EnterWorld(session);
            f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().Select(session);
            f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().EnsureInWorld(session);
            var entry = (await Enter(f, Entry() with { DungeonId = 122 })).Entry!;
            Assert.NotNull(entry);
            var state = State(f); var run = Runs(f).Find(new CharacterId(71))!;
            var actors = state.Plan.PathActors.Select(actor => actor.Sequence).ToArray();
            Assert.DoesNotContain(wireId, actors);
            Assert.Equal(actors, run.CurrentCellState.Actors.Select(actor => actor.Sequence));
            Assert.Equal(actors[0], run.CurrentCellState.FirstActorSequence);
            Assert.Equal(actors[0], entry.Visits[new(71)].FirstActorSequence);
            Runs(f).TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            Assert.Null((await Kill(f, wireId, session: session)).Death);
            Assert.Null((await Kill(f, actors[1], session: session)).Death);
            foreach (var actor in actors)
            {
                Assert.NotNull((await Kill(f, actor, session: session)).Death?.Change);
                Assert.Null((await Kill(f, actor, session: session)).Death);
            }
            Assert.Equal(TournamentStatus.Champion, State(f).Status);
        });
    }

    [SkippableFact]
    public async Task Black_dragon_nine_entries_are_transactional_persistent_projected_and_reset_at_six()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            var clock = new QuotaClock();
            await using var f = await Fixture(tickets: 0, configure: services => services.AddSingleton<TimeProvider>(clock));
            f.Sql("INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0); INSERT INTO character_pvp(character_id) VALUES(71); INSERT INTO character_settings(character_id) VALUES(71); INSERT INTO character_expert_job(character_id) VALUES(71)");
            var tournament = Tournament(f);
            async Task<Dfo.Server.Abstractions.World.TournamentQuotaSnapshot> Quota() => Assert.Single(await tournament.ReadQuotaAsync(new(71), default));
            Assert.Equal(9, (await Quota()).Remaining);
            Assert.NotNull((await Enter(f, Entry() with { DungeonId = 122 }, commit: false)).Entry);
            Assert.Equal(9, (await Quota()).Remaining);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='usage-quota.changed'"));
            for (var i = 0; i < 9; i++)
            {
                Assert.NotNull((await Enter(f, Entry() with { DungeonId = 122 })).Entry);
                var run = Runs(f).Find(new CharacterId(71))!;
                await Runs(f).EndAsync(new(71), run.RunId);
                Assert.Equal(8 - i, (await Quota()).Remaining);
            }
            Assert.Equal(DungeonEntryFailure.DailyEntryLimitReached, (await Enter(f, Entry() with { DungeonId = 122 })).Failure);
            Assert.Equal(9, (await Quota()).Used);
            Assert.Equal(9, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='usage-quota.changed'"));
            await using (var reload = new LearningFixture(existing: f.Path, useArchive: true, configure: services =>
            {
                services.AddSingleton(new PvfContentOptions { ArchivePath = ContentArchive.Path });
                services.AddSingleton<TimeProvider>(clock);
            }))
            {
                Assert.Equal(0, Assert.Single(await Tournament(reload).ReadQuotaAsync(new(71), default)).Remaining);
                var view = await reload.Services.GetRequiredService<ICharacterEntryStateDomainService>().LoadAsync(new(1), new(71), default);
                Assert.Equal(0, Assert.Single(view.Public.Extras.Dimensions, value => value.Key == 122).Value2);
            }
            clock.At = clock.At.AddMinutes(1);
            Assert.Equal(9, (await Quota()).Remaining);
            Assert.NotNull((await Enter(f, Entry() with { DungeonId = 122 })).Entry);
            Assert.Equal(8, (await Quota()).Remaining);
        });
    }

    private sealed class QuotaClock : TimeProvider
    {
        public DateTimeOffset At = new(2026, 10, 14, 5, 59, 0, TimeSpan.FromHours(8));
        public override DateTimeOffset GetUtcNow() => At;
    }

    [SkippableFact]
    public async Task Future_actor_and_duplicate_death_cannot_skip_rounds_or_spend_fatigue()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Ready(f);
            var future = await Kill(f, 2);
            Assert.Null(future.Death);
            Assert.Equal(0, State(f).CompletedRounds);
            Assert.False(Runs(f).Find(new CharacterId(71))!.CurrentCellState.FindActor(2)!.IsDead);
            Assert.NotNull((await Kill(f, 1)).Death?.Change);
            var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=71");
            var duplicate = await Kill(f, 1);
            Assert.Null(duplicate.Death);
            Assert.Equal(1, State(f).CompletedRounds);
            Assert.Equal(4, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
            Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
        });
    }

    [SkippableFact]
    public async Task Abandoned_entry_transaction_leaves_ticket_fatigue_and_run_unchanged()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            Assert.NotNull((await Enter(f, commit: false)).Entry);
            Assert.Null(Runs(f).Find(new CharacterId(71)));
            Assert.Null(Tournament(f).Find(new(71)));
            Assert.Equal(3, Count(f, 4726));
            Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
            Assert.NotNull((await Enter(f)).Entry);
            Assert.Equal(2, Count(f, 4726));
        });
    }

    [SkippableTheory]
    [InlineData("ticket", DungeonEntryFailure.MissingRequiredItem)]
    [InlineData("party", DungeonEntryFailure.MissingPrerequisite)]
    [InlineData("level", DungeonEntryFailure.LevelTooLow)]
    public async Task Missing_qualification_refuses_without_cost(string reason, DungeonEntryFailure failure)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture(tickets: reason == "ticket" ? 0 : 3);
            var context = Entry();
            if (reason == "party") context = context with
            {
                Participants = [new(new(1), new(71), 0, true), new(new(2), new(72), 1, false)],
            };
            if (reason == "level")
            {
                await using var tx = f.Scope.Begin();
                await f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71)).SetLevelAsync(68, default);
                await tx.CommitAsync(default);
            }
            var result = await Enter(f, context);
            Assert.Null(result.Entry);
            Assert.Equal(failure, result.Failure);
            Assert.Null(Runs(f).Find(new CharacterId(71)));
            Assert.Equal(reason == "ticket" ? 0 : 3, Count(f, 4726));
            Assert.Equal(0, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
        });
    }

    [SkippableFact]
    public async Task Each_reward_row_grants_once_repeated_selection_is_acknowledged_without_effect()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Champion(f);
            var rewards = State(f).Rewards;
            Assert.All(rewards[0], card => Assert.Equal(0, card.ItemId));
            Assert.All(rewards[1], card => Assert.Equal(3329, card.ItemId));
            var beforeGold = Gold(f);
            var first = Assert.IsType<TournamentActionResult>(await Select(f, 0, 0));
            Assert.Equal(beforeGold + rewards[0][0].Count, Gold(f));
            Assert.NotNull(first.GoldBalance);
            var item = Assert.IsType<TournamentActionResult>(await Select(f, 1, 0));
            Assert.NotNull(item.Delivery);
            Assert.Equal(rewards[1][0].Count, Count(f, 3329));
            var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=71");
            foreach (byte row in new byte[] { 0, 1 })
            {
                var duplicate = Assert.IsType<TournamentActionResult>(await Select(f, row, 1));
                Assert.Null(duplicate.Delivery);
                Assert.Null(duplicate.GoldBalance);
                Assert.Equal((byte)0, duplicate.State.Selections[row]);
            }
            Assert.Equal(beforeGold + rewards[0][0].Count, Gold(f));
            Assert.Equal(rewards[1][0].Count, Count(f, 3329));
            Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
            Assert.Null(await Select(f, 2, 0));
            Assert.Null(await Select(f, 0, 2));
        });
    }

    [SkippableFact]
    public async Task Reward_failure_after_delivery_rolls_back_inventory_and_selection_then_retry_grants_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture(configure: s => s.AddSingleton<IItemDeliveryDomainService>(provider =>
                new FailAfterDelivery(ActivatorUtilities.CreateInstance<ItemDeliveryDomainService>(provider))));
            await Champion(f);
            var reward = State(f).Rewards[1][0];
            var failure = (FailAfterDelivery)f.Services.GetRequiredService<IItemDeliveryDomainService>();
            failure.Fail = true;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await Select(f, 1, 0));
            Assert.Null(State(f).Selections[1]);
            Assert.Equal(0, Count(f, 3329));
            failure.Fail = false;
            Assert.NotNull((await Select(f, 1, 0))!.Delivery);
            Assert.Equal(reward.Count, Count(f, 3329));
            Assert.Null((await Select(f, 1, 1))!.Delivery);
            Assert.Equal(reward.Count, Count(f, 3329));
        });
    }

    [SkippableFact]
    public async Task Dead_participant_finishes_at_completed_round_reward_and_retains_run_for_selection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Ready(f);
            await Kill(f, 1);
            var run = Runs(f).Find(new CharacterId(71))!;
            Assert.Null(await Eliminate(f)); // A living participant cannot forfeit through the death timeout route.
            Runs(f).TryMarkParticipantDead(new(71), run.RunId, run.RoomVersion);
            TournamentActionResult? eliminated;
            await using (var tx = f.Scope.Begin())
            {
                eliminated = await Tournament(f).EliminateAsync(f.Session, run.RunId, default);
                await tx.CommitAsync(default);
            }
            Assert.NotNull(eliminated);
            Assert.Equal(TournamentStatus.Eliminated, State(f).Status);
            Assert.Equal(1, State(f).CompletedRounds);
            Assert.Equal(30000u, eliminated.Experience!.Breakdown.Base);
            Assert.Equal(2, State(f).Rewards[0].Count);
            Assert.Empty(State(f).Rewards[1]);
            Assert.Equal(run.RunId, Runs(f).Find(new CharacterId(71))!.RunId);
            Assert.True(Runs(f).Find(new CharacterId(71))!.ClearOpen);
            var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=71");
            Assert.Null(await Eliminate(f));
            Assert.Null((await Kill(f, 2)).Death);
            Assert.Null(await Select(f, 1, 0));
            Assert.NotNull(await Select(f, 0, 0));
            Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
        });
    }

    private static async Task<TournamentActionResult?> Eliminate(LearningFixture f)
    {
        await using var tx = f.Scope.Begin();
        var result = await Tournament(f).EliminateAsync(f.Session, State(f).RunId, default);
        await tx.CommitAsync(default);
        return result;
    }

    [SkippableFact]
    public async Task Unloaded_and_rolled_back_deaths_do_not_advance_or_charge_the_next_round()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Enter(f);
            Assert.Null((await Kill(f, 1)).Death);
            var run = Runs(f).Find(new CharacterId(71))!;
            Runs(f).TryMarkParticipantReady(new(71), run.RunId, run.RoomVersion);
            await Kill(f, 1, commit: false);
            Assert.Equal(0, State(f).CompletedRounds);
            Assert.False(Runs(f).Find(new CharacterId(71))!.CurrentCellState.FindActor(1)!.IsDead);
            Assert.Equal(2, (await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(new(71), default)).Used);
            Assert.NotNull((await Kill(f, 1)).Death?.Change);
            Assert.Equal(1, State(f).CompletedRounds);
        });
    }

    [SkippableTheory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public async Task Unsupported_modes_cannot_charge_a_tournament_ticket(byte mode, byte practice)
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            var attempt = await Enter(f, Entry() with { RequestMode = mode, PracticeMode = practice });
            Assert.Null(attempt.Entry);
            Assert.Equal(DungeonEntryFailure.MissingPrerequisite, attempt.Failure);
            Assert.Equal(3, Count(f, 4726));
        });
    }

    [SkippableFact]
    public async Task Automatic_flip_deadline_is_transactional_and_does_not_extend_on_repeated_status()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Champion(f);
            await using (var tx = f.Scope.Begin())
                Assert.True(Tournament(f).BeginRewardSelection(f.Session, State(f).RunId));
            Assert.Null(State(f).RewardSelectionEndsAt);
            var clock = f.Services.GetRequiredService<TimeProvider>();
            var startedAt = clock.GetUtcNow();
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(Tournament(f).BeginRewardSelection(f.Session, State(f).RunId));
                await tx.CommitAsync(default);
            }
            var deadline = State(f).RewardSelectionEndsAt!.Value;
            Assert.InRange(deadline, startedAt.AddSeconds(1), clock.GetUtcNow().AddSeconds(1));
            Assert.Empty(Tournament(f).PendingRewards(deadline.AddTicks(-1)));
            Assert.Single(Tournament(f).PendingRewards(deadline));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(Tournament(f).BeginRewardSelection(f.Session, State(f).RunId));
                await tx.CommitAsync(default);
            }
            Assert.Equal(deadline, State(f).RewardSelectionEndsAt);
            await Select(f, 0, 0);
            await Select(f, 1, 0);
            Assert.Empty(Tournament(f).PendingRewards(DateTimeOffset.MaxValue));
        });
    }

    [SkippableFact]
    public async Task Full_material_and_quick_slots_send_the_champion_material_to_one_mail()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = await Fixture();
            await Champion(f);
            var store = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(new(71), BusinessInventoryStoreKind.Item);
            var material = store.GetContainer(BusinessInventorySpace.Material, isDefault: true);
            var template = await f.Services.GetRequiredService<IItemDeliveryRuleService>().TemplateAsync(3330, default);
            await using (var tx = f.Scope.Begin())
            {
                foreach (var space in new[] { BusinessInventorySpace.Material, BusinessInventorySpace.QuickSlot })
                {
                    var container = store.GetContainer(space, isDefault: true);
                    var held = (await container.GetAllAsync(default)).Select(x => x.SlotIndex).ToHashSet();
                    for (var index = 0; index < await container.OpenSlotCountAsync(default); index++)
                    {
                        if (held.Contains(index)) continue;
                        var record = await f.Services.GetRequiredService<IItemLifecycleDomainService>()
                            .CreateAsync(template with { ItemUid = null, Count = 1 }, default);
                        await container.PutAsync(index, record, default);
                    }
                }
                await tx.CommitAsync(default);
            }
            var result = Assert.IsType<TournamentActionResult>(await Select(f, 1, 0));
            Assert.Empty(result.Delivery!.Inventory);
            Assert.Single(result.Delivery.MailIds);
            Assert.Equal(State(f).Rewards[1][0].Count, result.Delivery.MailedCount);
            Assert.Equal(0, Count(f, 3329));
            await using (var read = f.Scope.Begin())
            {
                var mail = Assert.Single(await f.Services.GetRequiredService<Dfo.Server.Abstractions.Mail.IMailboxDomainService>().InboxAsync(new(71)));
                Assert.Equal(new Dfo.Server.Data.Mail.MailAsset.Stackable(3329, State(f).Rewards[1][0].Count), Assert.Single(mail.Attachments).Asset);
            }
            Assert.Null((await Select(f, 1, 1))!.Delivery);
        });
    }

    private sealed class FailAfterDelivery(IItemDeliveryDomainService inner) : IItemDeliveryDomainService
    {
        public bool Fail { get; set; }
        public ValueTask<long> PendingQuantityAsync(CharacterId character, int itemId, CancellationToken ct) =>
            inner.PendingQuantityAsync(character, itemId, ct);
        public async ValueTask<ItemDeliveryReceipt> DeliverAsync(AccountId account, CharacterId character,
            IReadOnlyList<ItemDeliveryGrant> grants, string source, CancellationToken ct, ItemDeliveryMode mode, ItemDeliveryOptions? options = null)
        {
            var result = await inner.DeliverAsync(account, character, grants, source, ct, mode, options);
            if (Fail) throw new InvalidOperationException("Injected failure after staged tournament delivery.");
            return result;
        }
    }
}
