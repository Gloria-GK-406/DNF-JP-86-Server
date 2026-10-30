using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CycleDungeonEntry;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Quests;

public sealed partial class CycleQuestFlowTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Boss_clear_preserves_cycle_until_explicit_finish_or_give_up(bool hasMaterials, bool clearMap)
    {
        using var f = new ShopFixture(true,s =>
        {
            s.AddSingleton<IQuestContentRuleService>(new QuestRules { ClearMap = clearMap });
            s.AddSingleton<ICycleDungeonRuleService>(new CycleRules());
        });
        await AcceptCycle(f);
        f.Sql("INSERT INTO character_active_quests VALUES(1,5,2002,1);");
        if (hasMaterials) await f.Seed(121, 1000, 1, 3);
        var clear = new DungeonMapCleared(new(Guid.NewGuid()), 84, DungeonDifficulty.Normal,
            new(0, 0), 123, 1, f.Clock.GetUtcNow(), [new(new(1), new(1), 0, true)]) { IsBossRoom = true };
        for (var replay = 0; replay < 2; replay++)
        {
            await PublishClear(f, clear);
            await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("quest.map-clear");
            await f.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync("quest.progress-notification");
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2001"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2002"));
            Assert.Equal(hasMaterials ? 1 : 0, f.Count(1000));
            Assert.Equal(0, f.Count(5000));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.cycle-resolved'"));
            Assert.DoesNotContain(f.Sender.Messages, x => x is FinishQuestAcknowledgement or GiveUpQuestAcknowledgement);
        }
        if (clearMap)
            Assert.Equal(0, f.Scalar("SELECT trigger_value FROM character_active_quests WHERE quest_id=2001"));

        var commands = new QuestTestCommands(f);
        if (hasMaterials || clearMap)
        {
            var finished = await commands.SendAsync(new QuestFinishTestInput(new(1), new(1), 2001,
                ushort.MaxValue, 1, checked((uint)f.Clock.GetUtcNow().ToUnixTimeSeconds())));
            Assert.True(finished.Finished);
            Assert.Equal(1, f.Count(5000));
            Assert.Equal(0, f.Count(1000));
            Assert.False((await commands.SendAsync(new QuestFinishTestInput(new(1), new(1), 2001,
                ushort.MaxValue, 1, checked((uint)f.Clock.GetUtcNow().ToUnixTimeSeconds())))).Finished);
            Assert.Equal(1, f.Count(5000));
        }
        else
        {
            Assert.True((await commands.SendAsync(new QuestGiveUpTestInput(new(1), new(1), 2001))).GivenUp);
            Assert.Equal(0, f.Count(5000));
        }
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2001"));
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2002"));
    }

    private static async Task AcceptCycle(ShopFixture f)
    {
        var commands = new QuestTestCommands(f);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(f.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(f, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
    }

    private static async Task PublishClear(ShopFixture f, DungeonMapCleared clear)
    {
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using var tx = ((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            await f.Services.GetRequiredService<IEventService>().EnqueueAsync(clear);
            await tx.CommitAsync(default);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Party_cycle_accepts_the_missing_member_quest_only_when_common_admission_succeeds(bool admitted)
    {
        var visits = new RecordingVisits { Succeeds = admitted };
        using var fixture = PartyFixture(visits);
        var commands = new QuestTestCommands(fixture);
        TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        var entry = new DungeonEntryContext(84, new(DungeonDifficulty.Normal, [2001], [], DungeonEntryIntent.Cycle(84, 2001)),
            0, 0, [new(new(1), new(1), 0, true), new(new(2), new(3), 1, false)]);

        DungeonEntryAttempt result;
        result = await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = ((DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                result = await fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>().EnterAsync(entry, default);
                await tx.CommitAsync(default);
            }
            return result;
        });

        Assert.NotNull(visits.Facts);
        Assert.Equal(admitted, result.Entry is not null);
        Assert.Equal(admitted ? 1 : 0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=3 AND quest_id=2001"));
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1 AND quest_id=2001"));
        Assert.Equal(admitted ? 1 : 0, fixture.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'"));
    }

    [Fact]
    public async Task A_later_full_member_rolls_back_the_earlier_members_new_acceptance()
    {
        var visits = new RecordingVisits { Succeeds = true };
        using var fixture = PartyFixture(visits);
        fixture.Sql("INSERT INTO accounts(account_id,m_id) VALUES(3,'cycle-full'); INSERT INTO characters(character_id,account_id,name,job,level) VALUES(4,3,'full',12,2); INSERT INTO character_stats(character_id) VALUES(4);");
        for (var slot = 0; slot < 30; slot++) fixture.Sql($"INSERT INTO character_active_quests VALUES(4,{slot},{3000+slot},1);");
        var commands = new QuestTestCommands(fixture);
        TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        var entry = new DungeonEntryContext(84, new(DungeonDifficulty.Normal, [2001], [], DungeonEntryIntent.Cycle(84, 2001)),
            0, 0, [new(new(1), new(1), 0, true), new(new(2), new(3), 1, false), new(new(3), new(4), 2, false)]);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = ((DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                var result = await fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>().EnterAsync(entry, default);
                Assert.Equal(DungeonEntryFailure.MissingPrerequisite, result.Failure);
                Assert.Equal((byte)2, result.BlockedSlot);
                await tx.CommitAsync(default);
            }
        });
        Assert.Null(visits.Facts);
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=3"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_quest_acceptance_times WHERE character_id=3"));
        Assert.Equal(30, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=4"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='quest.progress-changed'"));
    }

    [Theory]
    [InlineData("ineligible")]
    [InlineData("full")]
    [InlineData("conflicting")]
    public async Task A_blocked_cycle_member_does_not_get_a_quest_or_lose_an_existing_one(string reason)
    {
        var visits = new RecordingVisits { Succeeds = true };
        using var fixture = PartyFixture(visits);
        var commands = new QuestTestCommands(fixture);
        TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        if (reason == "ineligible") fixture.Sql("UPDATE characters SET job=0 WHERE character_id=3;");
        if (reason == "conflicting") fixture.Sql("INSERT INTO character_active_quests VALUES(3,0,2002,7);");
        if (reason == "full")
            for (var i = 0; i < 30; i++) fixture.Sql($"INSERT INTO character_active_quests VALUES(3,{i},{3000+i},1);");
        var before = fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=3");
        var entry = new DungeonEntryContext(84, new(DungeonDifficulty.Normal, [2001], [], DungeonEntryIntent.Cycle(84, 2001)),
            0, 0, [new(new(1), new(1), 0, true), new(new(2), new(3), 1, false)]);
        DungeonEntryAttempt result;
        result = await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = ((DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                result = await fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>().EnterAsync(entry, default);
                await tx.CommitAsync(default);
            }
            return result;
        });
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, result.Failure);
        Assert.Equal((byte)1, result.BlockedSlot);
        Assert.Null(visits.Facts);
        Assert.Equal(before, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=3"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=3 AND quest_id=2001"));
    }

    [Fact]
    public async Task A_member_already_carrying_the_cycle_keeps_its_progress()
    {
        var visits = new RecordingVisits { Succeeds = true };
        using var fixture = PartyFixture(visits);
        var commands = new QuestTestCommands(fixture);
        TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        fixture.Sql("INSERT INTO character_active_quests VALUES(3,0,2001,0);");
        var entry = new DungeonEntryContext(84, new(DungeonDifficulty.Normal, [2001], [], DungeonEntryIntent.Cycle(84, 2001)),
            0, 0, [new(new(1), new(1), 0, true), new(new(2), new(3), 1, false)]);
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = ((DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                Assert.NotNull((await fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>().EnterAsync(entry, default)).Entry);
                await tx.CommitAsync(default);
            }
        });
        Assert.Equal(0, fixture.Scalar("SELECT trigger_value FROM character_active_quests WHERE character_id=3 AND quest_id=2001"));
    }

    private static ShopFixture PartyFixture(RecordingVisits visits)
    {
        var fixture = new ShopFixture(true,services =>
        {
            services.AddSingleton<IQuestContentRuleService>(new QuestRules());
            services.AddSingleton<ICycleDungeonRuleService>(new CycleRules { RequiredJob = 12 });
            services.AddSingleton<IDungeonVisitDomainService>(visits);
            services.AddSingleton<IDungeonDifficultyRuleService>(new DungeonDifficultyRuleService(TestRequired.Admissions(new CycleDefinitions())));
        });
        fixture.Sql("UPDATE characters SET level=2 WHERE character_id=1; INSERT INTO accounts(account_id,m_id) VALUES(2,'cycle-peer'); INSERT INTO characters(character_id,account_id,name,job,level) VALUES(3,2,'peer',12,2); INSERT INTO character_stats(character_id) VALUES(3);");
        return fixture;
    }

    [Fact]
    public async Task Preparing_accepting_finishing_and_repeating_are_separate_and_rewards_commit_once()
    {
        using var fixture = Fixture();
        var commands = new QuestTestCommands(fixture);
        var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        var accept = new QuestAcceptTestInput(new(1), new(1), 2001);
        Assert.False((await commands.SendAsync(accept)).Accepted);
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Interactive));
        var prepared = await PrepareWhole(fixture, 84, 2001);
        Assert.True(prepared.Accepted);
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        Assert.True((await commands.SendAsync(accept)).Accepted);
        Assert.Equal(QuestAcceptRefusal.AlreadyActive, (await commands.SendAsync(accept)).Refusal);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        await fixture.Seed(121, 1000, 1, 3);
        var finish = new QuestFinishTestInput(new(1), new(1), 2001, ushort.MaxValue, 1, 0);
        var result = await commands.SendAsync(finish);
        Assert.True(result.Finished);
        Assert.Equal(0, fixture.Count(1000));
        Assert.Equal(1, fixture.Count(5000));
        var update = Assert.Single(result.ConsumedItemUpdates);
        Assert.Equal(121, Assert.Single(update.Items).SlotIndex);
        Assert.Equal(-1, Assert.Single(update.Items).Core.ItemId);
        Assert.False((await commands.SendAsync(finish)).Finished);
        Assert.Equal(1, fixture.Count(5000));
        Assert.True((await commands.SendAsync(accept)).Accepted);
        Assert.False((await commands.SendAsync(finish)).Finished);
        Assert.Equal(1, fixture.Count(5000));
    }

    [Fact]
    public async Task Full_reward_space_finishes_the_cycle_and_mails_rewards()
    {
        using var fixture = Fixture();
        var commands = new QuestTestCommands(fixture);
        TestGameplay.Run(fixture.Services, () => fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        await fixture.Seed(121, 1000, 1, 3);
        foreach (var slot in new[] { 3, 4, 5, 6, 7, 8, 65, 66 }) await fixture.Seed(slot, 5000, 10);
        var result = await commands.SendAsync(new QuestFinishTestInput(new(1), new(1), 2001, ushort.MaxValue, 1, 0));
        Assert.True(result.Finished);
        Assert.Equal(0, fixture.Count(1000));
        Assert.Equal(80, fixture.Count(5000));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        Assert.True(fixture.Scalar("SELECT SUM(item_count) FROM mailbox_attachments WHERE claimed_flag=0") > 0);
    }

    [Fact]
    public async Task A_prepared_cycle_does_not_authorize_a_different_quest_or_stale_connection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = Fixture();
            var cycles = fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>();
            var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
            Assert.False((await cycles.PrepareAsync(fixture.Session, 84, 2001, default)).Accepted);
            intents.Record(fixture.Session, DungeonEntryIntent.Interactive);
            Assert.True((await cycles.PrepareAsync(fixture.Session, 84, 2001, default)).Accepted);
            Assert.False(await cycles.CanAcceptAsync(new(1), new(1), 2002, default));
            Assert.False((await cycles.PrepareAsync(fixture.Session with { ConnectionGeneration = 500 }, 84, 2001, default)).Accepted);
            Assert.True(await cycles.CanAcceptAsync(new(1), new(1), 2001, default));
        });
    }

    [Fact]
    public async Task Retry_prepares_a_new_pair_before_the_client_cancels_the_previous_quest()
    {
        using var fixture = Fixture();
        var commands = new QuestTestCommands(fixture);
        var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);

        // Native retry: 000F(previous map), 0308(new pair), 0020(old quest), 001F(new quest).
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Directed(84)));
        Assert.True((await PrepareWhole(fixture, 85, 2002)).Accepted);
        Assert.Equal(DungeonEntryIntent.Cycle(85, 2002), intents.Find(fixture.Session));
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2001"));
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE quest_id=2002"));
        Assert.False((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2002))).Accepted);
        Assert.True((await commands.SendAsync(new QuestGiveUpTestInput(new(1), new(1), 2001))).GivenUp);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2002))).Accepted);
        Assert.Equal(QuestAcceptRefusal.AlreadyActive, (await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2002))).Refusal);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));

        // The next retry can select the original pair without restarting the session.
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Directed(85)));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestGiveUpTestInput(new(1), new(1), 2002))).GivenUp);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
    }

    [Fact]
    public async Task Retry_cannot_rebind_an_unrelated_directed_map_or_an_invalid_cycle_pair()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = Fixture();
            var cycles = fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>();
            var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
            var unrelated = DungeonEntryIntent.Directed(10000);
            intents.Record(fixture.Session, unrelated);
            Assert.False((await cycles.PrepareAsync(fixture.Session, 85, 2002, default)).Accepted);
            Assert.Equal(unrelated, intents.Find(fixture.Session));
            var retry = DungeonEntryIntent.Directed(84);
            intents.Record(fixture.Session, retry);
            Assert.False((await cycles.PrepareAsync(fixture.Session, 85, 2001, default)).Accepted);
            Assert.False((await cycles.PrepareAsync(fixture.Session with { ConnectionGeneration = 500 }, 85, 2002, default)).Accepted);
            Assert.Equal(retry, intents.Find(fixture.Session));
            Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        });
    }

    [Fact]
    public async Task Retry_can_prepare_the_same_carried_cycle_but_acceptance_is_refused_without_resetting_it()
    {
        using var fixture = Fixture();
        var commands = new QuestTestCommands(fixture);
        var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Directed(84)));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        var replay = await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001));
        Assert.False(replay.Accepted);
        Assert.Equal(QuestAcceptRefusal.AlreadyActive, replay.Refusal);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
    }

    private static ShopFixture Fixture() => new(true,services =>
    {
        services.AddSingleton<IQuestContentRuleService>(new QuestRules());
        services.AddSingleton<ICycleDungeonRuleService>(new CycleRules());
    });

    [Theory]
    [InlineData(DungeonDifficulty.Normal, DungeonDifficulty.Normal, DungeonDifficulty.Normal)]
    [InlineData(DungeonDifficulty.Tier3, DungeonDifficulty.Adventure, DungeonDifficulty.Adventure)]
    [InlineData(DungeonDifficulty.Tier3, DungeonDifficulty.Tier3, DungeonDifficulty.Tier3)]
    [InlineData(DungeonDifficulty.Tier2, DungeonDifficulty.Tier3, DungeonDifficulty.Adventure)]
    public async Task Cycle_entry_uses_the_nearest_unlocked_map_difficulty_and_preserves_common_admission(
        DungeonDifficulty requested, DungeonDifficulty maximum, DungeonDifficulty expected)
    {
        var visits = new RecordingVisits();
        using var fixture = new ShopFixture(true,services =>
        {
            services.AddSingleton<IQuestContentRuleService>(new QuestRules());
            services.AddSingleton<ICycleDungeonRuleService>(new CycleRules());
            services.AddSingleton<IDungeonVisitDomainService>(visits);
            services.AddSingleton<IDungeonDifficultyRuleService>(new DungeonDifficultyRuleService(TestRequired.Admissions(new CycleDefinitions())));
        });
        var commands = new QuestTestCommands(fixture);
        var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        TestGameplay.Run(fixture.Services, () => intents.Record(fixture.Session, DungeonEntryIntent.Interactive));
        Assert.True((await PrepareWhole(fixture, 84, 2001)).Accepted);
        var intent = intents.Find(fixture.Session)!.Value;
        var entry = new DungeonEntryContext(84, new(requested, [2001], [], intent), 0, 0, [new(new(1), new(1), 0, true)]);
        var cycles = fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            await using (var tx = ((DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
            {
                await fixture.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>()
                    .UnlockAsync(new(1), 84, maximum, default);
                await tx.CommitAsync(default);
            }
        });

        // An incoming quest list cannot stand in for the persisted accepted quest.
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await cycles.EnterAsync(entry, default)).Failure);
        Assert.Null(visits.Facts);
        Assert.True((await commands.SendAsync(new QuestAcceptTestInput(new(1), new(1), 2001))).Accepted);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite,
            (await cycles.EnterAsync(entry with { RequestMode = 1 }, default)).Failure);
        Assert.Null(visits.Facts);
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await cycles.EnterAsync(entry with
        {
            Facts = entry.Facts with { Intent = DungeonEntryIntent.Cycle(85, 2001) }
        }, default)).Failure);
        Assert.Null(visits.Facts);

        var result = await cycles.EnterAsync(entry, default);
        Assert.Equal(DungeonEntryFailure.InsufficientFatigue, result.Failure);
        Assert.Equal(expected, visits.Facts!.Difficulty);
        Assert.Equal(intent, visits.Facts.Intent);
        Assert.Equal(requested, entry.Facts.Difficulty);
    }

    private sealed class CycleDefinitions : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken token) =>
            ValueTask.FromResult<DungeonDefinition?>(new(id, "dungeon/test-cycle.dgn", 1, true, [],
                SupportedDifficulties: [0, 1, 3]) { HasStoryMode = true });
    }

    private sealed class RecordingVisits : IDungeonVisitDomainService
    {
        public bool Succeeds { get; init; }
        public DungeonEntryFacts? Facts { get; private set; }
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
        public ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants,
            int dungeonId, DungeonEntryFacts facts, CancellationToken token)
        {
            Facts = facts;
            if (Succeeds)
                return ValueTask.FromResult(DungeonEntryAttempt.Accepted(new(
                    new(dungeonId, 0, 1, 1, new(0, 0), new(0, 0), 1, 0,
                        new Dictionary<MazeCell, DungeonCellDefinition>()), new Dictionary<CharacterId, DungeonRoomVisit>())));
            return ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.InsufficientFatigue));
        }
        public ValueTask<DungeonEntry?> EnterAsync(CharacterId characterId, int dungeonId,
            DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId, DungeonRunState current,
            MazeCell target, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class CycleRules : ICycleDungeonRuleService
    {
        public int? RequiredJob { get; init; }
        private static readonly CycleDungeonRegion Region = new(12, 101, 3, [new(84, 2001), new(85, 2002)], [new(5000, 1)]);
        public ValueTask<CycleDungeonRegion?> EligibleAsync(int dungeonId, int questId, WorldContentSubject subject, CancellationToken ct) =>
            ValueTask.FromResult<CycleDungeonRegion?>(
                (RequiredJob is null || subject.Job == RequiredJob) && Region.Candidates.Contains(new(dungeonId, questId)) ? Region : null);
        public IReadOnlyList<QuestRewardItemGrant> RewardsForQuest(int questId) => questId is 2001 or 2002 ? Region.Rewards : [];
    }

    private sealed class QuestRules : IQuestContentRuleService
    {
        public bool ClearMap { get; init; }
        private QuestDefinition Current => ClearMap ? Definition with
            { ObjectiveTypeCode = "clear map", ObjectiveArguments = [123], InitialTrigger = 1, SeekingItems = [] } : Definition;
        private static readonly QuestDefinition Definition = new(2001, "cycle seeking", QuestGrade.Circle, null,
            new LevelRange(1, 99), null, null, null, [new(QuestRewardKind.CircleDungeon, [0, 0])], "seeking", "M",
            null, null, null, null, null, null, null, [], [], [], [], SeekingItems: [new(1000, 1)]);
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Current, Current with { QuestId = 2002 }]);
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult<QuestDefinition?>(id is 2001 or 2002 ? Current with { QuestId = id } : null);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => new([0, 0], new Dictionary<char, int>(), 100, 100, 100, 100);
    }
}
