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
    public async Task Declared_entry_restrictions_require_their_complete_matching_state()
    {
        var service = Service(
            quests:
            [
                Quest(1),
                Quest(2, exposedByNpc: false),
                Quest(3, eventOnly: true),
                Quest(4, creatureKind: 11, creatureLevel: 5),
                Quest(5, expertJob: new ExpertJobRequirement(1, 1)),
                Quest(6, targetCharacter: new QuestTargetCharacterRestriction([QuestJobRequirement.Mage])),
                Quest(7, grade: QuestGrade.Training, npcIndex: 200),
                Quest(8, targetCharacter: new QuestTargetCharacterRestriction([QuestJobRequirement.Swordman])),
                Quest(9, grade: QuestGrade.Training, npcIndex: 201),
                Quest(10, creatureKind: 10, creatureLevel: 99),
            ],
            trainingAvailability: [new TrainingQuestAvailability(10, [201])],
            creatures:
            [
                Creature(10, [], [new CreatureEvolution(5, 999, 11)]),
            ],
            creatureIdByEquipment: new Dictionary<int, int> { [FarasItem] = 10 });

        var result = await service.LoadAsync(
            Subject(
                level: 10,
                job: 0,
                equippedCreatureItemId: FarasItem,
                equippedCreatureLevel: 5),
            CancellationToken.None);

        Assert.Equal([1, 8, 9], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task Creature_evolution_quest_requires_the_target_creatures_equipment_mapping()
    {
        var quest = Quest(10, creatureKind: 10, creatureLevel: 99);
        var currentCreature = Creature(10, [], [new CreatureEvolution(5, 999, 11)]);
        var targetCreature = Creature(11, []);
        var subject = Subject(
            level: 10,
            equippedCreatureItemId: FarasItem,
            equippedCreatureLevel: 5);

        var unresolved = await Service(
            quests: [quest],
            creatures: [currentCreature, targetCreature],
            creatureIdByEquipment: new Dictionary<int, int> { [FarasItem] = 10 })
            .LoadAsync(subject, CancellationToken.None);
        var resolved = await Service(
            quests: [quest],
            creatures: [currentCreature, targetCreature],
            creatureIdByEquipment: new Dictionary<int, int>
            {
                [FarasItem] = 10,
                [SilentItem] = 11,
            })
            .LoadAsync(subject, CancellationToken.None);

        Assert.Empty(unresolved.AvailableQuests.Quests);
        Assert.Equal([10], resolved.AvailableQuests.Quests.Select(candidate => candidate.QuestId));
    }

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

    /// <remarks>
    /// A repeatable quest is offered again after it is cleared; that is what makes it
    /// repeatable. Excluding every cleared quest retires the dailies permanently after one
    /// completion, and the character simply stops being offered them with no error anywhere.
    /// </remarks>
    [Theory]
    [InlineData(QuestGrade.Daily)]
    [InlineData(QuestGrade.NormallyRepeat)]
    [InlineData(QuestGrade.SpecialDaily)]
    public async Task A_repeatable_quest_is_offered_again_once_cleared(QuestGrade grade)
    {
        var service = Service(quests: [Quest(1, grade: grade)]);

        var result = await service.LoadAsync(Subject(clearedQuestIds: [1]), CancellationToken.None);

        Assert.Equal([1], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    /// <remarks>
    /// The legacy exemption names three grades and a random daily is not one of them, so it
    /// follows the ordinary cleared rule despite reading like a daily.
    /// </remarks>
    [Fact]
    public async Task A_random_daily_is_not_treated_as_repeatable()
    {
        var service = Service(quests: [Quest(1, grade: QuestGrade.DailyRandom)]);

        var result = await service.LoadAsync(Subject(clearedQuestIds: [1]), CancellationToken.None);

        Assert.Empty(result.AvailableQuests.Quests);
    }

    [Fact]
    public async Task Prerequisites_answers_and_collisions_use_the_complete_cleared_flag_map()
    {
        var service = Service(quests:
        [
            Quest(1),
            Quest(2, prerequisites: [[10, 11]]),
            Quest(3, prerequisites: [[10, 99], [12]]),
            Quest(4, prerequisites: [[10, 99], [98]]),
            Quest(5, requiredAnswers: [new QuestAnswerRequirement(11, 1)]),
            Quest(6, requiredAnswers: [new QuestAnswerRequirement(11, 0)]),
            Quest(7, collisions: [12]),
            Quest(8, collisions: [99]),
            Quest(9, accountCollisions: [10]),
            Quest(10),
            Quest(11, grade: QuestGrade.Daily),
        ]);

        var result = await service.LoadAsync(
            Subject(clearedQuestFlags: new Dictionary<int, int>
            {
                [10] = 1,
                [11] = 2,
                [12] = 1,
            }),
            CancellationToken.None);

        Assert.Equal([1, 2, 3, 5, 8, 11], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    /// <remarks>
    /// The content classifies quests that the entry list never carries — cracks, challenges,
    /// titles and the like are reached some other way. Offering them puts quests in front of
    /// a character the legacy server never shows them to.
    /// </remarks>
    [Theory]
    [InlineData(QuestGrade.Crack)]
    [InlineData(QuestGrade.Challenge)]
    [InlineData(QuestGrade.Title)]
    [InlineData(QuestGrade.Schedule)]
    [InlineData(QuestGrade.Urgent)]
    [InlineData(QuestGrade.Wedding)]
    [InlineData(QuestGrade.VillageAttackRevenge)]
    public async Task A_grade_the_entry_list_never_carries_is_not_offered(QuestGrade grade)
    {
        var service = Service(quests: [Quest(1, grade: grade), Quest(2, grade: QuestGrade.Side)]);

        var result = await service.LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal([2], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Theory]
    [InlineData(QuestGrade.Normal)]
    [InlineData(QuestGrade.Side)]
    [InlineData(QuestGrade.Sub)]
    [InlineData(QuestGrade.Epic)]
    [InlineData(QuestGrade.Achievement)]
    [InlineData(QuestGrade.CommonUnique)]
    [InlineData(QuestGrade.System)]
    public async Task A_grade_the_entry_list_carries_is_offered(QuestGrade grade)
    {
        var service = Service(quests: [Quest(1, grade: grade)]);

        var result = await service.LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal([1], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
    }

    [Fact]
    public async Task An_absent_grade_is_unrestricted_but_an_unrecognized_declaration_is_rejected()
    {
        var service = Service(quests:
        [
            Quest(1, grade: null),
            Quest(2, grade: QuestGrade.Unrecognized),
        ]);

        var result = await service.LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal([1], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
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
    public async Task Available_quests_keep_first_authoritative_client_range_order()
    {
        var service = Service(quests:
        [
            Quest(7),
            Quest(0),
            Quest(3),
            Quest(7),
            Quest(30_000),
            Quest(9),
        ]);

        var result = await service.LoadAsync(Subject(), CancellationToken.None);

        Assert.Equal([7, 3, 9], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
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

    [Fact]
    public async Task Mixed_all_and_specific_job_declaration_retains_the_specific_family_restriction()
    {
        var service = Service(quests:
        [
            Quest(1, jobRestriction: new QuestJobRestriction(
                IsExactAll: false,
                [QuestJobRequirement.Swordman])),
        ]);

        var swordman = await service.LoadAsync(Subject(job: 0), CancellationToken.None);
        var mage = await service.LoadAsync(Subject(job: 3), CancellationToken.None);

        Assert.Equal([1], swordman.AvailableQuests.Quests.Select(quest => quest.QuestId));
        Assert.Empty(mage.AvailableQuests.Quests);
    }

    [Fact]
    public async Task Job_restrictions_distinguish_optional_exact_all_specific_and_invalid_states()
    {
        var service = Service(quests:
        [
            Quest(1),
            Quest(2, jobRestriction: new QuestJobRestriction(IsExactAll: true, [])),
            Quest(3, jobRestriction: new QuestJobRestriction(
                IsExactAll: false,
                [QuestJobRequirement.Swordman])),
            Quest(4, jobRestriction: new QuestJobRestriction(IsExactAll: false, [])),
            Quest(5, jobRestriction: new QuestJobRestriction(
                IsExactAll: false,
                [QuestJobRequirement.Mage])),
        ]);

        var swordman = await service.LoadAsync(Subject(job: 0), CancellationToken.None);
        var mage = await service.LoadAsync(Subject(job: 3), CancellationToken.None);

        Assert.Equal([1, 2, 3], swordman.AvailableQuests.Quests.Select(quest => quest.QuestId));
        Assert.Equal([1, 2, 5], mage.AvailableQuests.Quests.Select(quest => quest.QuestId));
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
    public async Task Job_change_quests_preserve_masked_exempt_and_ordinary_grow_branches()
    {
        var service = Service(quests:
        [
            Quest(1),
            Quest(2, growType: 3, jobChangeQuestCode: 2),
            Quest(3, growType: 0x23, jobChangeQuestCode: 2),
            Quest(4, growType: 99, jobChangeQuestCode: 1),
            Quest(5, growType: 99, jobChangeQuestCode: 10),
            Quest(6, growType: 99, jobChangeQuestCode: 20),
            Quest(7, growType: 0x23),
            Quest(8, growType: 3),
        ]);

        var result = await service.LoadAsync(Subject(growType: 0x23), CancellationToken.None);

        Assert.Equal([1, 2, 4, 5, 6, 7], result.AvailableQuests.Quests.Select(quest => quest.QuestId));
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

    /// <remarks>
    /// Equipment and creature identifiers are disjoint in the archive — the creature list
    /// tops out near twenty thousand and creature equipment starts above sixty-three
    /// thousand, with no overlap at all. Every case below therefore equips an item
    /// identifier, because that is what the inventory holds, and an implementation that
    /// looked it up as a creature identifier would report "no pet" for everyone.
    /// </remarks>
    private const int FarasItem = 63000;
    private const int SilentItem = 63001;
    private const int UnlistedItem = 63999;

    [Fact]
    public async Task An_empty_creature_slot_means_no_welcome_rather_than_an_empty_one()
    {
        var service = Service(
            creatures: [Creature(10, ["hello"])],
            creatureIdByEquipment: new Dictionary<int, int> { [FarasItem] = 10 });

        var result = await service.LoadAsync(
            Subject(equippedCreatureItemId: 0),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    [Fact]
    public async Task An_equipped_creature_with_no_script_means_no_welcome()
    {
        var service = Service(
            creatures: [Creature(10, [])],
            creatureIdByEquipment: new Dictionary<int, int> { [SilentItem] = 10 });

        var result = await service.LoadAsync(
            Subject(equippedCreatureItemId: SilentItem),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    [Fact]
    public async Task An_equipped_item_that_denotes_no_creature_means_no_welcome()
    {
        var service = Service(
            creatures: [Creature(10, ["hello"])],
            creatureIdByEquipment: new Dictionary<int, int> { [FarasItem] = 10 });

        var result = await service.LoadAsync(
            Subject(equippedCreatureItemId: UnlistedItem),
            CancellationToken.None);

        Assert.Null(result.PetWelcome);
    }

    /// <remarks>
    /// The regression this pins: an implementation that passes the equipped item straight to
    /// the creature lookup finds nothing, and the nothing is indistinguishable from an
    /// ordinary character with no pet. Equipping item 63000 while creature 10 exists would
    /// pass under that implementation only if the test also confused the two spaces.
    /// </remarks>
    [Fact]
    public async Task An_equipped_creature_item_resolves_through_the_equipment_hop_to_its_lines()
    {
        var service = Service(
            creatures: [Creature(10, ["hello", "again"])],
            creatureIdByEquipment: new Dictionary<int, int> { [FarasItem] = 10 });

        var result = await service.LoadAsync(
            Subject(equippedCreatureItemId: FarasItem),
            CancellationToken.None);

        Assert.NotNull(result.PetWelcome);
        var welcome = result.PetWelcome!;
        Assert.Equal(10, welcome.CreatureId);
        Assert.Equal(["hello", "again"], welcome.Lines);
    }

    [Fact]
    public async Task An_equipped_item_is_never_read_as_a_creature_identifier()
    {
        // Creature 63000 exists, and item 63000 denotes creature 10. Reading the item as a
        // creature identifier would return creature 63000's lines instead of creature 10's.
        var service = Service(
            creatures: [Creature(10, ["correct"]), Creature(FarasItem, ["wrong"])],
            creatureIdByEquipment: new Dictionary<int, int> { [FarasItem] = 10 });

        var result = await service.LoadAsync(
            Subject(equippedCreatureItemId: FarasItem),
            CancellationToken.None);

        Assert.Equal(["correct"], result.PetWelcome!.Lines);
    }

    private static WorldContentEntryRuleService Service(
        IReadOnlyList<QuestDefinition>? quests = null,
        IReadOnlyList<TrainingQuestAvailability>? trainingAvailability = null,
        IReadOnlyList<DailyActivityGroupDefinition>? dailyActivities = null,
        IReadOnlyList<CollectionBoxDefinition>? collectionBoxes = null,
        IReadOnlyList<CreatureDefinition>? creatures = null,
        IReadOnlyDictionary<int, int>? creatureIdByEquipment = null) =>
        new(new StubQuestSource(quests ?? [], trainingAvailability ?? []),
            new StubDailyActivitySource(dailyActivities ?? []),
            new StubCollectionBoxSource(collectionBoxes ?? []),
            new StubCreatureSource(creatures ?? [], creatureIdByEquipment));

    private static WorldContentSubject Subject(
        int level = 1,
        byte job = 0,
        byte growType = 0,
        IReadOnlyCollection<int>? clearedQuestIds = null,
        IReadOnlyDictionary<int, int>? clearedQuestFlags = null,
        IReadOnlyCollection<int>? claimedDailyChallengeGroupIds = null,
        IReadOnlyList<CollectionBoxProgressSubject>? collectionBoxes = null,
        int equippedCreatureItemId = 0,
        int equippedCreatureLevel = 0) =>
        new(level,
            job,
            growType,
            clearedQuestFlags ?? (clearedQuestIds ?? []).ToDictionary(questId => questId, _ => 1),
            claimedDailyChallengeGroupIds ?? [],
            collectionBoxes ?? [],
            equippedCreatureItemId,
            equippedCreatureLevel);

    private static QuestDefinition Quest(
        int questId,
        LevelRange? level = null,
        QuestJobRequirement job = QuestJobRequirement.Any,
        QuestJobRestriction? jobRestriction = null,
        int growType = QuestDefinition.AnyGrowType,
        QuestGrade? grade = QuestGrade.Side,
        bool? exposedByNpc = null,
        bool? eventOnly = null,
        int? creatureKind = null,
        int? creatureLevel = null,
        ExpertJobRequirement? expertJob = null,
        QuestTargetCharacterRestriction? targetCharacter = null,
        int? jobChangeQuestCode = null,
        IReadOnlyList<IReadOnlyList<int>>? prerequisites = null,
        IReadOnlyList<QuestAnswerRequirement>? requiredAnswers = null,
        IReadOnlyList<int>? collisions = null,
        IReadOnlyList<int>? accountCollisions = null,
        int? npcIndex = 0) =>
        new(questId,
            $"quest-{questId}",
            grade,
            jobRestriction ?? (job is QuestJobRequirement.Any
                ? null
                : new QuestJobRestriction(IsExactAll: false, [job])),
            level ?? LevelRange.Any,
            growType == QuestDefinition.AnyGrowType ? null : growType,
            NpcIndex: npcIndex,
            CompleteNpcIndex: 0,
            Rewards: [],
            ObjectiveTypeCode: "seeking",
            DifficultyCode: "a",
            IsExposedByNpc: exposedByNpc,
            IsEventOnly: eventOnly,
            CreatureKindRequirement: creatureKind,
            CreatureLevelRequirement: creatureLevel,
            ExpertJobRequirement: expertJob,
            TargetCharacterRestriction: targetCharacter,
            JobChangeQuestCode: jobChangeQuestCode,
            CompletedQuestPrerequisiteGroups: prerequisites ?? [],
            RequiredQuestAnswers: requiredAnswers ?? [],
            CollisionQuestIds: collisions ?? [],
            AccountCollisionQuestIds: accountCollisions ?? []);

    private static DailyActivityGroupDefinition Group(int groupId, LevelRange level) =>
        new(groupId, level, [], [], []);

    private static CreatureDefinition Creature(
        int creatureId,
        IReadOnlyList<string> ambientLines,
        IReadOnlyList<CreatureEvolution>? evolutions = null) =>
        new(creatureId,
            $"creature-{creatureId}",
            StartLevel: 1,
            PermissionLevel: 1,
            MaximumLevel: 10,
            ArtifactSlots: [],
            Evolutions: evolutions ?? [],
            AmbientLines: ambientLines);

    private sealed class StubQuestSource(
        IReadOnlyList<QuestDefinition> quests,
        IReadOnlyList<TrainingQuestAvailability> trainingAvailability) : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(quests);

        public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(quests.FirstOrDefault(quest => quest.QuestId == questId));

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) => ValueTask.FromResult(trainingAvailability);
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

    /// <remarks>
    /// The equipment index is kept deliberately separate from the creature identifiers, and
    /// the two use disjoint number ranges, mirroring the archive. A stub that resolved
    /// equipment by creature identifier would let the id-space confusion this models pass
    /// unnoticed.
    /// </remarks>
    private sealed class StubCreatureSource(
        IReadOnlyList<CreatureDefinition> creatures,
        IReadOnlyDictionary<int, int>? creatureIdByEquipment = null) : ICreatureDefinitionSource
    {
        public ValueTask<IReadOnlyList<CreatureDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(creatures);

        public ValueTask<CreatureDefinition?> GetAsync(int creatureId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(creatures.FirstOrDefault(creature => creature.CreatureId == creatureId));

        public ValueTask<CreatureDefinition?> FindByEquipmentAsync(
            int itemTemplateId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                creatureIdByEquipment is not null
                && creatureIdByEquipment.TryGetValue(itemTemplateId, out var creatureId)
                    ? creatures.FirstOrDefault(creature => creature.CreatureId == creatureId)
                    : null);

        public ValueTask<bool> CanResolveEvolutionEquipmentAsync(
            int currentEquipmentItemId,
            int targetCreatureId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                creatureIdByEquipment?.Values.Contains(targetCreatureId) is true);
    }
}
