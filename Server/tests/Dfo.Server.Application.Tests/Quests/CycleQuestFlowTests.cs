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
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class CycleQuestFlowTests
{
    [Fact]
    public async Task Preparing_accepting_finishing_and_repeating_are_separate_and_rewards_commit_once()
    {
        using var fixture = Fixture();
        var commands = fixture.Services.GetRequiredService<ICommandBus>();
        var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        var accept = new AcceptQuestCommand(new(1), new(1), 2001);
        Assert.False((await commands.SendAsync(accept)).Accepted);
        intents.Record(fixture.Session, DungeonEntryIntent.Interactive);
        var prepared = await commands.SendAsync(new PrepareCycleDungeonEntryCommand(fixture.Session, 84, 2001));
        Assert.True(prepared.Accepted);
        Assert.Equal(0, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        Assert.True((await commands.SendAsync(accept)).Accepted);
        Assert.True((await commands.SendAsync(accept)).Accepted);
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
        await fixture.Seed(121, 1000, 1, 3);
        var finish = new FinishQuestCommand(new(1), new(1), 2001, ushort.MaxValue, 1, 0);
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
    public async Task Full_reward_space_preserves_the_cycle_quest_and_its_material()
    {
        using var fixture = Fixture();
        var commands = fixture.Services.GetRequiredService<ICommandBus>();
        fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Record(fixture.Session, DungeonEntryIntent.Interactive);
        Assert.True((await commands.SendAsync(new PrepareCycleDungeonEntryCommand(fixture.Session, 84, 2001))).Accepted);
        Assert.True((await commands.SendAsync(new AcceptQuestCommand(new(1), new(1), 2001))).Accepted);
        await fixture.Seed(121, 1000, 1, 3);
        foreach (var slot in new[] { 3, 4, 5, 6, 7, 8, 65, 66 }) await fixture.Seed(slot, 5000, 10);
        var result = await commands.SendAsync(new FinishQuestCommand(new(1), new(1), 2001, ushort.MaxValue, 1, 0));
        Assert.False(result.Finished);
        Assert.Equal(1, fixture.Count(1000));
        Assert.Equal(80, fixture.Count(5000));
        Assert.Equal(1, fixture.Scalar("SELECT COUNT(*) FROM character_active_quests"));
    }

    [Fact]
    public async Task A_prepared_cycle_does_not_authorize_a_different_quest_or_stale_connection()
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
    }

    private static ShopFixture Fixture() => new(services =>
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
        using var fixture = new ShopFixture(services =>
        {
            services.AddSingleton<IQuestContentRuleService>(new QuestRules());
            services.AddSingleton<ICycleDungeonRuleService>(new CycleRules());
            services.AddSingleton<IDungeonVisitDomainService>(visits);
            services.AddSingleton<IDungeonDifficultyRuleService>(new DungeonDifficultyRuleService(new CycleDefinitions()));
        });
        var commands = fixture.Services.GetRequiredService<ICommandBus>();
        var intents = fixture.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
        intents.Record(fixture.Session, DungeonEntryIntent.Interactive);
        Assert.True((await commands.SendAsync(new PrepareCycleDungeonEntryCommand(fixture.Session, 84, 2001))).Accepted);
        var intent = intents.Find(fixture.Session)!.Value;
        var entry = new DungeonEntryContext(84, new(requested, [2001], [], intent), 0, 0, [new(new(1), new(1), 0, true)]);
        var cycles = fixture.Services.GetRequiredService<ICycleDungeonEntryDomainService>();
        await using (var tx = ((DomainTransactionScope)fixture.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
        {
            await fixture.Services.GetRequiredService<ICharacterDungeonDifficultyDomainService>()
                .UnlockAsync(new(1), 84, maximum, default);
            await tx.CommitAsync(default);
        }

        // An incoming quest list cannot stand in for the persisted accepted quest.
        Assert.Equal(DungeonEntryFailure.MissingPrerequisite, (await cycles.EnterAsync(entry, default)).Failure);
        Assert.Null(visits.Facts);
        Assert.True((await commands.SendAsync(new AcceptQuestCommand(new(1), new(1), 2001))).Accepted);
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
        public DungeonEntryFacts? Facts { get; private set; }
        public bool DefersSelectionState(byte level, bool tutorialCompleted = false) => false;
        public ValueTask<DungeonEntryAttempt> TryEnterPartyAsync(IReadOnlyList<DungeonRunParticipant> participants,
            int dungeonId, DungeonEntryFacts facts, CancellationToken token)
        {
            Facts = facts;
            return ValueTask.FromResult(DungeonEntryAttempt.Refused(DungeonEntryFailure.InsufficientFatigue));
        }
        public ValueTask<DungeonEntry?> EnterAsync(CharacterId characterId, int dungeonId,
            DungeonDifficulty difficulty, DungeonEntryFacts facts, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<DungeonRoomVisit?> MoveAsync(CharacterId characterId, DungeonRunState current,
            MazeCell target, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class CycleRules : ICycleDungeonRuleService
    {
        private static readonly CycleDungeonRegion Region = new(12, 101, 3, [new(84, 2001)], [new(5000, 1)]);
        public ValueTask<CycleDungeonRegion?> EligibleAsync(int dungeonId, int questId, WorldContentSubject subject, CancellationToken ct) =>
            ValueTask.FromResult<CycleDungeonRegion?>(dungeonId == 84 && questId == 2001 ? Region : null);
        public IReadOnlyList<QuestRewardItemGrant> RewardsForQuest(int questId) => questId == 2001 ? Region.Rewards : [];
    }

    private sealed class QuestRules : IQuestContentRuleService
    {
        private static readonly QuestDefinition Definition = new(2001, "cycle seeking", QuestGrade.Circle, null,
            new LevelRange(1, 99), null, null, null, [new(QuestRewardKind.CircleDungeon, [0, 0])], "seeking", "M",
            null, null, null, null, null, null, null, [], [], [], [], SeekingItems: [new(1000, 1)]);
        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Definition]);
        public ValueTask<QuestDefinition?> FindAsync(int id, CancellationToken ct) => ValueTask.FromResult<QuestDefinition?>(id == 2001 ? Definition : null);
        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
        public QuestParameters Parameters() => new([0, 0], new Dictionary<char, int>(), 100, 100, 100, 100);
    }
}
