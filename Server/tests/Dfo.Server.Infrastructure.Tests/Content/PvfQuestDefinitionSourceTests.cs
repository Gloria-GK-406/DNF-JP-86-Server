using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Quests;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// Locates the operator-supplied archive. It is excluded from version control, so these
/// tests skip rather than fail where it is absent.
/// </summary>
internal static class ContentArchive
{
    public static string? Path { get; } = Locate();

    public static bool Available => Path is not null;

    public const string SkipReason =
        "Script.pvf not found. Set PVF_ARCHIVE_PATH, or place the archive in the repository root or Data/Pvf/.";

    public static PvfArchiveProvider CreateProvider() =>
        new(new PvfContentOptions { ArchivePath = Path });

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable(PvfContentOptions.PathVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var candidate in new[]
                     {
                         System.IO.Path.Combine(directory.FullName, "Script.pvf"),
                         System.IO.Path.Combine(directory.FullName, "Data", "Pvf", "Script.pvf"),
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed class PvfQuestDefinitionSourceTests
{
    [SkippableFact]
    public async Task Story_stage_is_independent_of_the_regions_shared_minimum_level()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var definitions = await new PvfQuestDefinitionSource(provider).GetAllAsync(default);
        Assert.Contains(definitions, q => q.StoryStageLevel == 17 && q.RequiredLevel?.Minimum == 17);
        Assert.Contains(definitions, q => q.StoryStageLevel == 19 && q.RequiredLevel?.Minimum == 17);
    }

    [Fact]
    public void Monster_reward_operands_are_strict_seven_field_groups()
    {
        Assert.Equal(
        [
            new QuestEnemyRewardItem(65601, QuestEnemyType.Monster, 145, -1, 10149163, 1, 100, 15),
            new QuestEnemyRewardItem(65305, QuestEnemyType.Monster, 145, -1, 10149163, 1, 100, 15),
        ],
            PvfQuestDefinitionSource.ProjectMonsterRewardItems(
                [65601, 145, -1, 10149163, 1, 100, 15,
                 65305, 145, -1, 10149163, 1, 100, 15],
                questId: 1779));

        Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectMonsterRewardItems(
                [65601, 145, -1, 10149163, 1, 100],
                questId: 1779));
        Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectMonsterRewardItems(
                [65601, 145, -1, 10149163, 0, 100, 15],
                questId: 1779));
    }

    [Fact]
    public void Seeking_operands_preserve_gold_and_seek_meet_uses_only_item_pair()
    {
        Assert.Equal(
            [new SeekingItemRequirement(0, 100), new SeekingItemRequirement(123, 2)],
            PvfQuestDefinitionSource.ProjectSeekingItems("seeking", [0, 100, 123, 2, -1, 9]));
        Assert.Equal(
            [new SeekingItemRequirement(77, 3)],
            PvfQuestDefinitionSource.ProjectSeekingItems("seek n meet npc", [77, 3, 99]));
        Assert.Empty(PvfQuestDefinitionSource.ProjectSeekingItems("meet npc", [77, 3]));
    }

    [Fact]
    public void ItemRewardOperandsPreserveConditionsSelectionsAndGoldSemantics()
    {
        var number = PvfQuestDefinitionSource.ItemRewardOperand.Number;
        var job = PvfQuestDefinitionSource.ItemRewardOperand.JobMarker;

        var reward = PvfQuestDefinitionSource.ProjectItemRewards(
            [number(0), number(0), number(10), number(2), number(20), job,
             number(0), number(-1), number(1)],
            [number(30), job, number(0), number(2), number(4)],
            questId: 7);

        Assert.True(reward.HasGoldFormulaMarker);
        Assert.Equal(0, reward.FixedGold);
        Assert.Equal(
            [new QuestItemRewardRule(10, 2), new(20, 1, new(0, -1))],
            reward.Fixed);
        Assert.Equal(
            [new QuestItemRewardRule(30, 4, new(0, 2))],
            reward.Selectable);
    }

    [Fact]
    public void MalformedItemRewardOperandsFailClosed()
    {
        var number = PvfQuestDefinitionSource.ItemRewardOperand.Number;
        var job = PvfQuestDefinitionSource.ItemRewardOperand.JobMarker;

        Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectItemRewards(
                [number(10), job, number(0), number(-1)], [], questId: 8));
        Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectItemRewards(
                [], [number(0), number(5)], questId: 9));
    }

    [Fact]
    public void Job_restriction_preserves_exact_all_and_rejects_malformed_declarations()
    {
        var exactAll = PvfQuestDefinitionSource.ProjectJobRestriction("[all]", 1);
        var mixed = PvfQuestDefinitionSource.ProjectJobRestriction("[all] [swordman]", 2);
        var family = PvfQuestDefinitionSource.ProjectJobRestriction("[mage]", 3);

        Assert.True(exactAll.IsExactAll);
        Assert.True(exactAll.IsValid);
        Assert.Empty(exactAll.JobFamilies);
        Assert.False(mixed.IsExactAll);
        Assert.True(mixed.IsValid);
        Assert.Equal([QuestJobRequirement.Swordman], mixed.JobFamilies);
        Assert.False(family.IsExactAll);
        Assert.True(family.IsValid);
        Assert.Equal([QuestJobRequirement.Mage], family.JobFamilies);
        Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectJobRestriction("[all] [all]", 4));
        Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectJobRestriction("[all] [unknown]", 5));
    }

    [Fact]
    public void Malformed_training_block_does_not_produce_a_mapping()
    {
        var normalized = PvfQuestDefinitionSource.NormalizeTrainingBlocks(
        [
            [40, null, 500],
            [41, 501],
            [42, 0, 502],
        ]);

        var valid = Assert.Single(normalized);
        Assert.Equal(41, valid.CharacterLevel);
        Assert.Equal([501], valid.NpcIndexes);
    }

    [Fact]
    public void Declared_target_without_a_modelled_family_is_not_optional_absence()
    {
        Assert.Null(PvfQuestDefinitionSource.ProjectTargetCharacterRestriction(
            isDeclared: false,
            targetCharacterText: null));

        var malformed = PvfQuestDefinitionSource.ProjectTargetCharacterRestriction(
            isDeclared: true,
            targetCharacterText: null);

        Assert.NotNull(malformed);
        Assert.False(malformed.IsValid);
        Assert.Empty(malformed.JobFamilies);
    }

    [Fact]
    public void List_identity_keeps_first_client_range_occurrence_in_authoritative_order()
    {
        var normalized = PvfQuestDefinitionSource.NormalizeListEntries(
        [
            (0, "zero.qst"),
            (7, "first-seven.qst"),
            (3, "three.qst"),
            (7, "duplicate-seven.qst"),
            (30_000, "too-large.qst"),
            (-1, "negative.qst"),
            (9, "nine.qst"),
        ]);

        Assert.Equal(
            [(7, "first-seven.qst"), (3, "three.qst"), (9, "nine.qst")],
            normalized);
    }

    [SkippableFact]
    public async Task Projects_every_neutral_eligibility_input_from_the_real_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quests = await source.GetAllAsync(CancellationToken.None);
        var training = await source.GetTrainingAvailabilityAsync(CancellationToken.None);
        var questIds = quests.Select(quest => quest.QuestId).ToHashSet();

        // Snapshot: server PVF SHA-256 C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06.
        // The unified kill-objective parser retains regional/five-field declarations that the
        // former four-field initial-trigger projection excluded. Keep exact catalogue counts
        // alongside the field-level checks so accidental projection changes remain visible.
        // 3253 existing quests plus 42 Circle quests with job-filtered reward declarations.
        // Exclude the separately verified optional dragon restoration from this old snapshot.
        Assert.Equal(3_295, quests.Count(quest => quest.QuestId is < 20720 or > 20735));
        Assert.Equal(quests.Count, questIds.Count);
        Assert.All(quests, quest => Assert.InRange(quest.QuestId, 1, 29_999));
        Assert.Equal(444, quests.Count(quest => quest.IsExposedByNpc.HasValue));
        Assert.Equal(351, quests.Count(quest => quest.IsEventOnly.HasValue));
        Assert.Equal(351, quests.Count(quest => quest.IsEventOnly == true));
        Assert.Equal(15, quests.Count(quest => quest.CreatureKindRequirement.HasValue));
        Assert.Equal(6, quests.Count(quest => quest.ExpertJobRequirement is not null));
        Assert.Equal(147, quests.Count(quest => quest.TargetCharacterRestriction is not null));
        Assert.DoesNotContain(
            quests,
            quest => quest.TargetCharacterRestriction?.IsValid == false);
        Assert.Contains(quests, quest => quest.JobChangeQuestCode.HasValue);
        Assert.Contains(quests, quest => quest.CompletedQuestPrerequisiteGroups.Count > 0);
        Assert.Contains(quests, quest => quest.RequiredQuestAnswers.Count > 0);
        Assert.Contains(quests, quest => quest.CollisionQuestIds.Count > 0);
        Assert.DoesNotContain(quests, quest => quest.AccountCollisionQuestIds.Count > 0);
        Assert.True((await source.GetAsync(13_502, CancellationToken.None))?.HasSpecialRewardStatus);
        Assert.False((await source.GetAsync(13_501, CancellationToken.None))?.HasSpecialRewardStatus);
        Assert.All(
            quests.SelectMany(quest => quest.CompletedQuestPrerequisiteGroups).SelectMany(group => group),
            prerequisiteId => Assert.InRange(prerequisiteId, 1, 29_999));
        Assert.All(
            quests.SelectMany(quest => quest.RequiredQuestAnswers),
            answer =>
            {
                Assert.InRange(answer.QuestId, 1, 29_999);
                Assert.True(answer.AnswerIndex >= 0);
            });
        Assert.All(
            quests.SelectMany(quest => quest.CollisionQuestIds),
            collisionId => Assert.InRange(collisionId, 1, 29_999));

        // The real TrainingQuest.lst contains level scalars but no NPC operands. The
        // legacy parser therefore yields no exact level/NPC mapping, and so does this
        // neutral source; treating the level itself as an NPC would fabricate permission.
        Assert.Empty(training);
    }

    [SkippableFact]
    public async Task Real_archive_projects_populated_seeking_operands()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quests = await new PvfQuestDefinitionSource(provider).GetAllAsync(CancellationToken.None);

        var seeking = quests.Where(quest => quest.SeekingItems is { Count: > 0 }).ToArray();
        Assert.NotEmpty(seeking);
        Assert.All(seeking.SelectMany(quest => quest.SeekingItems!), requirement =>
        {
            Assert.True(requirement.ItemId >= 0);
            Assert.True(requirement.RequiredCount > 0);
        });
    }

    [SkippableFact]
    public async Task Real_archive_projects_quest_1779_monster_item_sources_exactly()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quest = await new PvfQuestDefinitionSource(provider)
            .GetAsync(1779, CancellationToken.None);

        Assert.NotNull(quest);
        Assert.Equal(
        [
            new QuestEnemyRewardItem(65601, QuestEnemyType.Monster, 145, -1, 10149163, 1, 100, 15),
            new QuestEnemyRewardItem(65305, QuestEnemyType.Monster, 145, -1, 10149163, 1, 100, 15),
        ],
            quest.EnemyRewardItems);
        Assert.Equal([new SeekingItemRequirement(10149163, 15)], quest.SeekingItems);
    }

    [Fact]
    public void Relationship_validation_preserves_alternatives_and_ignores_unknown_collisions()
    {
        var result = PvfQuestDefinitionSource.NormalizeRelationships(
            ownerQuestId: 10,
            completedQuestGroups: [[1, 2], [3], [999]],
            requiredAnswerGroups: [[4, 0, 5, 2]],
            collisionQuestGroups: [[6, 999, 6]],
            accountCollisionQuestGroups: [[]],
            knownQuestIds: new HashSet<int> { 1, 2, 3, 4, 5, 6, 10 });

        Assert.NotNull(result);
        Assert.Equal(2, result.CompletedQuestGroups.Count);
        Assert.Equal([1, 2], result.CompletedQuestGroups[0]);
        Assert.Equal([3], result.CompletedQuestGroups[1]);
        Assert.Equal(
            [new QuestAnswerRequirement(4, 0), new QuestAnswerRequirement(5, 2)],
            result.RequiredAnswers);
        Assert.Equal([6], result.CollisionQuestIds);
        Assert.Empty(result.AccountCollisionQuestIds);
    }

    [Theory]
    [MemberData(nameof(InvalidRelationshipDeclarations))]
    public void Malformed_mandatory_relationships_reject_the_owner_definition(
        int ownerQuestId,
        IReadOnlyList<IReadOnlyList<int>> completedQuestGroups,
        IReadOnlyList<IReadOnlyList<int>> requiredAnswerGroups,
        IReadOnlyList<IReadOnlyList<int>> collisionQuestGroups,
        IReadOnlySet<int> knownQuestIds)
    {
        Assert.Null(PvfQuestDefinitionSource.NormalizeRelationships(
            ownerQuestId,
            completedQuestGroups,
            requiredAnswerGroups,
            collisionQuestGroups,
            [],
            knownQuestIds));
    }

    public static TheoryData<
        int,
        IReadOnlyList<IReadOnlyList<int>>,
        IReadOnlyList<IReadOnlyList<int>>,
        IReadOnlyList<IReadOnlyList<int>>,
        IReadOnlySet<int>> InvalidRelationshipDeclarations => new()
        {
            { 10, [[0]], [], [], new HashSet<int> { 10 } },
            { 10, [[10]], [], [], new HashSet<int> { 10 } },
            { 10, [[999]], [], [], new HashSet<int> { 10 } },
            { 10, [], [[1]], [], new HashSet<int> { 1, 10 } },
            { 10, [], [[999, 0]], [], new HashSet<int> { 10 } },
            { 10, [], [[1, -1]], [], new HashSet<int> { 1, 10 } },
            { 10, [], [], [[10]], new HashSet<int> { 10 } },
        };

    [SkippableFact]
    public async Task Projects_every_quest_the_list_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quests = await source.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(quests);
        Assert.All(quests, quest => Assert.True(quest.QuestId > 0));
        Assert.All(quests, quest => Assert.NotNull(quest.Name));

        // Some content declares no name at all -- tower placeholders, for instance -- so a
        // blanket non-empty assertion would encode a rule the archive does not follow.
        // What must hold is that named quests keep their name.
        Assert.Contains(quests, quest => quest.Name.Length > 0);
    }

    [SkippableFact]
    public async Task Projects_a_known_quest_field_by_field()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quest = await source.GetAsync(1, CancellationToken.None);
        Skip.If(quest is null, "This archive does not define quest 1.");

        // n_quest/Title/achievement_34_title.qst
        Assert.Equal(QuestGrade.Achievement, quest!.Grade);
        Assert.NotNull(quest.JobRestriction);
        Assert.True(quest.JobRestriction!.IsExactAll);
        Assert.True(quest.JobRestriction.IsValid);
        Assert.Empty(quest.JobRestriction.JobFamilies);
        Assert.Equal(new LevelRange(33, 99), quest.RequiredLevel);
        Assert.Null(quest.GrowTypeRequirement);
        Assert.True(quest.AppliesToAnyGrowType);
        Assert.Equal(3, quest.NpcIndex);
        Assert.Null(quest.CompleteNpcIndex);

        var reward = Assert.Single(quest.Rewards);
        Assert.Equal(QuestRewardKind.Title, reward.Kind);
        Assert.Equal([26607, 1], reward.Values);
    }

    [SkippableFact]
    public async Task Quest1776PreservesItsClientReportedPassiveObjectTarget()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quest = await source.GetAsync(1776, CancellationToken.None);

        Assert.NotNull(quest);
        Assert.Equal("hunt enemy", quest!.ObjectiveTypeCode);
        var target = Assert.Single(quest.KillObjectives.Targets);
        Assert.Equal(QuestKillParseStatus.Parsed, quest.KillObjectives.Status);
        Assert.Equal(new QuestKillScope(QuestKillScopeKind.Dungeon, 144), target.Scope);
        Assert.Equal(new QuestKillTarget(QuestKillTargetKind.Exact, 13099, HuntEnemyTargetTypes.PassiveObject), target.Target);
        Assert.Equal(1, target.RequiredCount);
    }

    [SkippableFact]
    public async Task Quest1781ProjectsOnlyItsSubtype6DungeonClearTarget()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quest = await new PvfQuestDefinitionSource(provider)
            .GetAsync(1781, CancellationToken.None);

        Assert.NotNull(quest);
        Assert.Equal(new DungeonClearRequirement(146, -1), quest.DungeonClearRequirement);
        Assert.Null(PvfQuestDefinitionSource.ProjectDungeonClearRequirement(
            "condition under clear", [4], [146, -1]));
        Assert.Null(PvfQuestDefinitionSource.ProjectDungeonClearRequirement(
            "condition under clear", [6], [146, -1, 1]));
    }

    [SkippableFact]
    public async Task Quest1776DoesNotFlattenEveryJobsEquipmentIntoJobZeroRewards()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quest = await source.GetAsync(1776, CancellationToken.None);
        var reward = new QuestRewardRuleService().Resolve(
            quest!,
            new PvfQuestParameterSource(provider).Load(),
            new(Job: 0, GrowType: 0, Level: 1),
            rewardSelection: ushort.MaxValue,
            completionCount: 1);

        Assert.Equal(14, quest!.ItemRewards.Fixed.Count);
        Assert.Empty(quest.ItemRewards.Selectable);
        Assert.True(reward.IsValid);
        Assert.Equal([new QuestRewardItemGrant(101010437, 1)], reward.Plan!.Items);
    }

    [SkippableFact]
    public async Task Quest130DeclaresFormulaGoldThroughItsMultiplierWithoutAnItemZeroMarker()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quest = await new PvfQuestDefinitionSource(provider)
            .GetAsync(130, CancellationToken.None);

        Assert.NotNull(quest);
        Assert.Equal(200, quest!.GoldMultiplier);
        Assert.False(quest.ItemRewards.HasGoldFormulaMarker);
    }

    [SkippableFact]
    public async Task Every_job_and_grade_token_in_the_archive_is_modelled()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quests = await source.GetAllAsync(CancellationToken.None);

        // An Unrecognized value means the archive uses a token this contract does not
        // model, which would silently drop a distinction the content draws.
        Assert.DoesNotContain(
            quests,
            quest => quest.JobRestriction?.JobFamilies.Contains(QuestJobRequirement.Unrecognized) is true);
        Assert.DoesNotContain(quests, quest => quest.Grade == QuestGrade.Unrecognized);
        Assert.DoesNotContain(
            quests,
            quest => quest.Rewards.Any(reward => reward.Kind == QuestRewardKind.Unrecognized));
    }

    [SkippableFact]
    public async Task Repeated_reads_return_the_same_catalog_without_reprojecting()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var first = await source.GetAllAsync(CancellationToken.None);
        var second = await source.GetAllAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [SkippableFact]
    public async Task Vocabulary_values_cross_the_boundary_without_archive_spelling()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quests = await source.GetAllAsync(CancellationToken.None);

        // Bracket delimiters are how the archive spells a token, not part of the value.
        Assert.All(quests, quest => Assert.DoesNotContain('[', quest.ObjectiveTypeCode));
        Assert.All(quests, quest => Assert.DoesNotContain(']', quest.ObjectiveTypeCode));
        Assert.Contains(quests, quest => quest.ObjectiveTypeCode == "seeking");
    }

    [SkippableFact]
    public async Task An_undefined_quest_returns_null_rather_than_a_placeholder()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        Assert.Null(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }

    [SkippableFact]
    public void A_missing_archive_fails_with_an_actionable_message()
    {
        using var provider = new PvfArchiveProvider(
            new PvfContentOptions { ArchivePath = "/definitely/not/here/Script.pvf" });

        // Resolution falls through to the conventional locations, so this only proves the
        // failure shape when no archive is reachable at all.
        Skip.If(ContentArchive.Available, "An archive is reachable from this working tree.");

        var error = Assert.Throws<FileNotFoundException>(() => provider.Archive);
        Assert.Contains("PVF_ARCHIVE_PATH", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// SC-6: the definitions must carry enough to drive matrix row 12, which needs an
    /// <em>available</em> quest list rather than a raw catalog. The availability rule
    /// itself belongs to G5; this only proves the contract does not starve it.
    /// </summary>
    [SkippableFact]
    public async Task Definitions_support_computing_an_available_quest_list()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);
        var quests = await source.GetAllAsync(CancellationToken.None);

        // Stand-in for character progress that G3 will supply.
        var character = new { Level = 40, Job = QuestJobRequirement.Mage, GrowType = 1 };
        var cleared = quests.Take(50).Select(quest => quest.QuestId).ToHashSet();

        var available = quests
            .Where(quest => !cleared.Contains(quest.QuestId))
            .Where(quest => quest.RequiredLevel?.Includes(character.Level) ?? true)
            .Where(quest => quest.JobRestriction is null
                || quest.JobRestriction.IsExactAll
                || quest.JobRestriction.JobFamilies.Contains(character.Job))
            .Where(quest => quest.AppliesToAnyGrowType || quest.GrowTypeRequirement == character.GrowType)
            .ToList();

        Assert.NotEmpty(available);
        Assert.DoesNotContain(available, quest => cleared.Contains(quest.QuestId));
        Assert.All(available, quest => Assert.True(quest.RequiredLevel?.Includes(40) ?? true));
        Assert.All(
            available,
            quest => Assert.True(
                quest.JobRestriction is null
                || quest.JobRestriction.IsExactAll
                || quest.JobRestriction.JobFamilies.Contains(QuestJobRequirement.Mage)));

        // A level-1 character must see a different set, or the level band is not being read.
        var lowLevel = quests.Count(quest => quest.RequiredLevel?.Includes(1) ?? true);
        Assert.NotEqual(lowLevel, quests.Count(quest => quest.RequiredLevel?.Includes(40) ?? true));
    }
}
