using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The rules that turn static definitions into a character's entry answers.
/// </summary>
/// <remarks>
/// These run against controlled definitions so each rule can be isolated. That the same
/// rules survive contact with the real archive is proven separately in
/// <see cref="WorldContentArchiveTests"/>.
/// </remarks>
public sealed class WorldContentRuleTests
{
    [Fact]
    public async Task Available_quests_exclude_the_ones_the_character_has_cleared()
    {
        var service = Service(quests: new[]
        {
            Quest(1),
            Quest(2),
            Quest(3),
        });

        var result = await service.LoadAsync(Subject(clearedQuestIds: [2]), CancellationToken.None);

        Assert.Equal([1, 3], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task Available_quests_respect_the_level_band()
    {
        var service = Service(quests: new[]
        {
            Quest(1, level: new LevelRange(1, 10)),
            Quest(2, level: new LevelRange(40, 50)),
        });

        var lowLevel = await service.LoadAsync(Subject(level: 5), CancellationToken.None);
        var highLevel = await service.LoadAsync(Subject(level: 45), CancellationToken.None);

        Assert.Equal([1], lowLevel.AvailableQuests.Quests.Select(quest => quest.QuestId));
        Assert.Equal([2], highLevel.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task A_quest_open_to_any_job_is_offered_regardless_of_the_characters_job()
    {
        var service = Service(quests: new[]
        {
            Quest(1, job: QuestJobRequirement.Any),
            Quest(2, job: QuestJobRequirement.Mage),
        });

        var result = await service.LoadAsync(Subject(job: 0), CancellationToken.None);

        Assert.Equal([1], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    /// <remarks>
    /// The job numbering is not a straight index: two pairs collapse onto one family each.
    /// A regression here would silently offer the wrong quest list, so the whole mapping is
    /// pinned rather than sampled.
    /// </remarks>
    [Theory]
    [InlineData(0, QuestJobRequirement.Swordman)]
    [InlineData(1, QuestJobRequirement.Fighter)]
    [InlineData(2, QuestJobRequirement.Gunner)]
    [InlineData(3, QuestJobRequirement.Mage)]
    [InlineData(4, QuestJobRequirement.Priest)]
    [InlineData(5, QuestJobRequirement.AwakenedGunner)]
    [InlineData(6, QuestJobRequirement.Thief)]
    [InlineData(7, QuestJobRequirement.AwakenedFighter)]
    [InlineData(8, QuestJobRequirement.AwakenedMage)]
    [InlineData(9, QuestJobRequirement.AwakenedSwordman)]
    [InlineData(10, QuestJobRequirement.AwakenedMage)]
    [InlineData(11, QuestJobRequirement.AwakenedSwordman)]
    [InlineData(12, QuestJobRequirement.Knight)]
    public async Task A_job_number_selects_the_job_family_the_legacy_index_assigns_it(
        byte job,
        QuestJobRequirement expected)
    {
        var service = Service(quests: [Quest(1, job: expected)]);

        var result = await service.LoadAsync(Subject(job: job), CancellationToken.None);

        Assert.Equal([1], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task An_unmapped_job_number_is_offered_no_job_restricted_quest()
    {
        var service = Service(quests:
        [
            Quest(1, job: QuestJobRequirement.Swordman),
            Quest(2, job: QuestJobRequirement.Any),
        ]);

        var result = await service.LoadAsync(Subject(job: 200), CancellationToken.None);

        Assert.Equal([2], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task A_quest_bound_to_a_grow_type_is_offered_only_to_that_grow_type()
    {
        var service = Service(quests:
        [
            Quest(1, growType: QuestDefinition.AnyGrowType),
            Quest(2, growType: 3),
        ]);

        var matching = await service.LoadAsync(Subject(growType: 3), CancellationToken.None);
        var other = await service.LoadAsync(Subject(growType: 4), CancellationToken.None);

        Assert.Equal([1, 2], matching.AvailableQuests.Quests.Select(quest => quest.QuestId));
        Assert.Equal([1], other.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task A_daily_group_opens_only_the_slots_the_characters_level_allows()
    {
        var service = Service(dailyActivities:
        [
            new DailyActivityGroupDefinition(
                GroupId: 7,
                Level: new LevelRange(1, 99),
                SlotAllowances: [new DailyActivitySlotAllowance(new LevelRange(50, 99), 2)],
                Slots:
                [
                    new DailyActivitySlot(0, [100]),
                    new DailyActivitySlot(1, [101]),
                    new DailyActivitySlot(2, [102]),
                ],
                Rewards: []),
        ]);

        var result = await service.LoadAsync(Subject(level: 60), CancellationToken.None);

        var group = Assert.Single(result.DailyChallenge.Groups);
        Assert.Equal(2, group.OpenSlotCount);
        Assert.Equal([0, 1], group.OpenSlots.Select(slot => slot.SlotIndex));
    }

    [Fact]
    public async Task A_daily_group_outside_the_characters_level_is_not_offered()
    {
        var service = Service(dailyActivities:
        [
            Group(1, new LevelRange(1, 20)),
            Group(2, new LevelRange(60, 99)),
        ]);

        var result = await service.LoadAsync(Subject(level: 10), CancellationToken.None);

        Assert.Equal([1], result.DailyChallenge.Groups.Select(group => group.GroupId));
    }

    [Fact]
    public async Task A_claimed_daily_group_is_reported_as_claimed()
    {
        var service = Service(dailyActivities: [Group(7, new LevelRange(1, 99))]);

        var result = await service.LoadAsync(
            Subject(claimedDailyChallengeGroupIds: [7]),
            CancellationToken.None);

        Assert.True(Assert.Single(result.DailyChallenge.Groups).RewardClaimed);
    }

    [Fact]
    public async Task A_daily_group_reports_the_reward_band_matching_the_characters_level()
    {
        var service = Service(dailyActivities:
        [
            new DailyActivityGroupDefinition(
                GroupId: 7,
                Level: new LevelRange(1, 99),
                SlotAllowances: [],
                Slots: [],
                Rewards:
                [
                    new DailyActivityReward(new LevelRange(1, 49), 500, 1),
                    new DailyActivityReward(new LevelRange(50, 99), 900, 3),
                ]),
        ]);

        var result = await service.LoadAsync(Subject(level: 60), CancellationToken.None);

        var group = Assert.Single(result.DailyChallenge.Groups);
        Assert.Equal(900, group.RewardItemTemplateId);
        Assert.Equal(3, group.RewardCount);
    }

    [Fact]
    public async Task A_collection_box_marks_exactly_the_positions_the_character_has_collected()
    {
        var service = Service(collectionBoxes:
        [
            new CollectionBoxDefinition(
                BoxIndex: 3,
                Columns: 2,
                Rows: 2,
                BaseExpiresAt: null,
                MaximumExpiresAt: null,
                Slots:
                [
                    new CollectionBoxSlot(0, 0, 1000, 1),
                    new CollectionBoxSlot(1, 0, 1001, 1),
                    new CollectionBoxSlot(0, 1, 1002, 2),
                    new CollectionBoxSlot(1, 1, 1003, 2),
                ],
                Extensions: []),
        ]);

        var result = await service.LoadAsync(
            Subject(collectionBoxes: [new CollectionBoxProgressSubject(3, [1000, 1003])]),
            CancellationToken.None);

        var box = Assert.Single(result.CollectionBoxes);
        Assert.Equal(2, box.FilledCount);
        Assert.Equal(
            [1000, 1003],
            box.Cells.Where(cell => cell.Filled).Select(cell => cell.ItemTemplateId));
    }

    [Fact]
    public async Task A_collection_box_the_character_has_no_progress_in_is_reported_wholly_unfilled()
    {
        var service = Service(collectionBoxes:
        [
            new CollectionBoxDefinition(3, 1, 1, null, null, [new CollectionBoxSlot(0, 0, 1000, 1)], []),
        ]);

        var result = await service.LoadAsync(Subject(), CancellationToken.None);

        var box = Assert.Single(result.CollectionBoxes);
        Assert.Equal(0, box.FilledCount);
        Assert.Single(box.Cells);
    }

    [Fact]
    public async Task No_equipped_creature_means_no_welcome_rather_than_an_empty_one()
    {
        var service = Service(creatures: [Creature(10, ["hello"])]);

        var result = await service.LoadAsync(
            Subject(equippedCreatureId: 0),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    [Fact]
    public async Task An_equipped_creature_with_no_script_means_no_welcome()
    {
        var service = Service(creatures: [Creature(10, [])]);

        var result = await service.LoadAsync(
            Subject(equippedCreatureId: 10),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    [Fact]
    public async Task An_equipped_creature_the_content_does_not_define_means_no_welcome()
    {
        var service = Service(creatures: [Creature(10, ["hello"])]);

        var result = await service.LoadAsync(
            Subject(equippedCreatureId: 999),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    [Fact]
    public async Task An_equipped_creature_with_a_script_carries_its_lines()
    {
        var service = Service(creatures: [Creature(10, ["hello", "again"])]);

        var result = await service.LoadAsync(
            Subject(equippedCreatureId: 10),
            CancellationToken.None);

        Assert.NotNull(result.PetWelcome);
        var welcome = result.PetWelcome!;
        Assert.Equal(10, welcome.CreatureId);
        Assert.Equal(["hello", "again"], welcome.Lines);
    }

    private static WorldContentEntryService Service(
        IReadOnlyList<QuestDefinition>? quests = null,
        IReadOnlyList<DailyActivityGroupDefinition>? dailyActivities = null,
        IReadOnlyList<CollectionBoxDefinition>? collectionBoxes = null,
        IReadOnlyList<CreatureDefinition>? creatures = null) =>
        new(new StubQuestSource(quests ?? []),
            new StubDailyActivitySource(dailyActivities ?? []),
            new StubCollectionBoxSource(collectionBoxes ?? []),
            new StubCreatureSource(creatures ?? []));

    private static WorldContentSubject Subject(
        int level = 1,
        byte job = 0,
        byte growType = 0,
        IReadOnlyCollection<int>? clearedQuestIds = null,
        IReadOnlyCollection<int>? claimedDailyChallengeGroupIds = null,
        IReadOnlyList<CollectionBoxProgressSubject>? collectionBoxes = null,
        int equippedCreatureId = 0) =>
        new(level,
            job,
            growType,
            clearedQuestIds ?? [],
            claimedDailyChallengeGroupIds ?? [],
            collectionBoxes ?? [],
            equippedCreatureId);

    private static QuestDefinition Quest(
        int questId,
        LevelRange? level = null,
        QuestJobRequirement job = QuestJobRequirement.Any,
        int growType = QuestDefinition.AnyGrowType) =>
        new(questId,
            $"quest-{questId}",
            QuestGrade.Side,
            job,
            level ?? LevelRange.Any,
            growType,
            NpcIndex: 0,
            CompleteNpcIndex: 0,
            Rewards: [],
            ObjectiveTypeCode: "seeking",
            DifficultyCode: "a");

    private static DailyActivityGroupDefinition Group(int groupId, LevelRange level) =>
        new(groupId, level, [], [], []);

    private static CreatureDefinition Creature(int creatureId, IReadOnlyList<string> ambientLines) =>
        new(creatureId,
            $"creature-{creatureId}",
            StartLevel: 1,
            PermissionLevel: 1,
            MaximumLevel: 10,
            ArtifactSlots: [],
            Evolutions: [],
            AmbientLines: ambientLines);

    private sealed class StubQuestSource(IReadOnlyList<QuestDefinition> quests) : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(quests);

        public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(quests.FirstOrDefault(quest => quest.QuestId == questId));
    }

    private sealed class StubDailyActivitySource(IReadOnlyList<DailyActivityGroupDefinition> groups)
        : IDailyActivityDefinitionSource
    {
        public ValueTask<IReadOnlyList<DailyActivityGroupDefinition>> GetAllAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(groups);

        public ValueTask<DailyActivityGroupDefinition?> GetAsync(
            int groupId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(groups.FirstOrDefault(group => group.GroupId == groupId));
    }

    private sealed class StubCollectionBoxSource(IReadOnlyList<CollectionBoxDefinition> boxes)
        : ICollectionBoxDefinitionSource
    {
        public ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(boxes);

        public ValueTask<CollectionBoxDefinition?> GetAsync(
            int boxIndex,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(boxes.FirstOrDefault(box => box.BoxIndex == boxIndex));
    }

    private sealed class StubCreatureSource(IReadOnlyList<CreatureDefinition> creatures)
        : ICreatureDefinitionSource
    {
        public ValueTask<IReadOnlyList<CreatureDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(creatures);

        public ValueTask<CreatureDefinition?> GetAsync(int creatureId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(creatures.FirstOrDefault(creature => creature.CreatureId == creatureId));
    }
}
