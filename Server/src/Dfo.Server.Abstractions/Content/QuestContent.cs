namespace Dfo.Server.Abstractions.Content;

/// <summary>An inclusive character-level band.</summary>
public readonly record struct LevelRange(int Minimum, int Maximum)
{
    /// <summary>A band that admits any level.</summary>
    public static LevelRange Any => new(0, int.MaxValue);

    public bool Includes(int level) => level >= Minimum && level <= Maximum;
}

/// <summary>
/// Which job family a quest is offered to.
/// </summary>
/// <remarks>
/// These are the distinctions the content itself draws, enumerated from every quest in
/// the content itself. Whether a given character qualifies is a rule, and lives above
/// this contract.
/// </remarks>
public enum QuestJobRequirement
{
    /// <summary>The content named a job this contract does not model.</summary>
    Unrecognized = 0,
    Any,
    Swordman,
    Fighter,
    Gunner,
    Mage,
    Priest,
    Thief,
    Knight,
    DemonicSwordman,
    AwakenedSwordman,
    AwakenedFighter,
    AwakenedGunner,
    AwakenedMage,
    AwakenedPriest,
    AwakenedThief,
    AwakenedKnight,
}

/// <summary>The content's own classification of a quest.</summary>
public enum QuestGrade
{
    Unrecognized = 0,
    Normal,
    Training,
    Epic,
    Achievement,
    Crack,
    Challenge,
    CommonUnique,
    Title,
    Side,
    Daily,
    DailyRandom,
    SpecialDaily,
    NormallyRepeat,
    Sub,
    System,
    Schedule,
    Urgent,
    Wedding,
    VillageAttackRevenge,
}

/// <summary>A declared quest job restriction after archive spelling is removed.</summary>
public sealed record QuestJobRestriction(
    bool IsExactAll,
    IReadOnlyList<QuestJobRequirement> JobFamilies)
{
    public bool IsValid => IsExactAll
        ? JobFamilies.Count == 0
        : JobFamilies.Count > 0
            && JobFamilies.All(family => family is not (
                QuestJobRequirement.Any or QuestJobRequirement.Unrecognized));
}

/// <summary>What a quest hands out on completion.</summary>
public enum QuestRewardKind
{
    Unrecognized = 0,
    Item,
    Title,
    GrowType,
    AwakeningType,
    CreatureEvolution,
    EventCreatureEvolution,
    Ridable,
    ExpertJob,
    EventSkill,
    HellChallenge,
    SlotExpansion,
    CrackOfDimension,
}

/// <summary>One reward the quest grants, with the content's own operands.</summary>
public sealed record QuestReward(QuestRewardKind Kind, IReadOnlyList<int> Values);

/// <summary>A job and grow-type condition attached to one item reward.</summary>
public sealed record QuestRewardCharacterCondition(int JobId, int GrowType);

/// <summary>One declared item/count reward, optionally restricted to one character career.</summary>
public sealed record QuestItemRewardRule(
    int ItemId,
    int Count,
    QuestRewardCharacterCondition? Condition = null);

/// <summary>The two item-reward lists and the distinct gold declaration carried beside them.</summary>
public sealed record QuestItemRewardDefinition(
    IReadOnlyList<QuestItemRewardRule> Fixed,
    IReadOnlyList<QuestItemRewardRule> Selectable,
    bool HasGoldFormulaMarker,
    int FixedGold)
{
    public static QuestItemRewardDefinition Empty { get; } = new([], [], false, 0);
}

/// <summary>A quest restriction on one expert-job type and its minimum level.</summary>
public sealed record ExpertJobRequirement(int ExpertJobType, int MinimumLevel);

/// <summary>A required answer to a prior question quest.</summary>
public sealed record QuestAnswerRequirement(int QuestId, int AnswerIndex);

/// <summary>One inventory quantity that contributes to an item-seeking quest.</summary>
public sealed record SeekingItemRequirement(int ItemId, int RequiredCount);

/// <summary>
/// A declared target-character restriction. An empty family list is an explicitly
/// malformed declaration, distinct from the nullable field's unrestricted absence.
/// </summary>
public sealed record QuestTargetCharacterRestriction(
    IReadOnlyList<QuestJobRequirement> JobFamilies)
{
    public bool IsValid => JobFamilies.Count > 0;
}

/// <summary>NPCs that can offer training quests at one character level.</summary>
public sealed record TrainingQuestAvailability(int CharacterLevel, IReadOnlyList<int> NpcIndexes);

/// <summary>
/// A quest as the content defines it, with no character progress mixed in.
/// </summary>
/// <remarks>
/// Combining this with a character's cleared-quest state to decide what is actually
/// offerable is a rule, not a lookup, and belongs to the service that owns quest state.
/// </remarks>
/// <param name="Name">
/// The quest's display name, or empty. Some content, such as tower placeholder quests,
/// genuinely declares no name; that is not a projection failure.
/// </param>
/// <param name="DifficultyCode">
/// A single-letter grading the content applies to the quest. Its meaning is not confirmed,
/// so it is deliberately not given a business name.
/// </param>
/// <param name="PaysAtCharacterLevel">
/// Whether the quest is paid at the finishing character's level rather than its own, with no
/// penalty for having outgrown it. The archive spells this <c>[ignore quest level 4 exp]</c>, and
/// declares it on 5 per cent of its quests -- the achievement family, mostly, which stays worth
/// finishing at any level.
/// </param>
/// <param name="ObjectiveTypeCode">
/// What the quest asks the character to do, as a bare vocabulary value such as
/// <c>seeking</c> or <c>meet npc</c>. It is passed through rather than modelled as an
/// enumeration because the full vocabulary is not yet confirmed.
/// </param>
/// <summary>The archive's actor family in one <c>[hunt enemy]</c> target.</summary>
public static class HuntEnemyTargetTypes
{
    public const int Monster = 1;
    public const int AiCharacter = 2;
    public const int PassiveObject = 3;
}

/// <summary>
/// One <c>[hunt enemy]</c> target. <paramref name="EnemyType"/> is the actor family above; it is
/// not the monster rank carried by room actors.
/// </summary>
public sealed record HuntEnemyRequirement(
    int DungeonId,
    int Difficulty,
    int EnemyCode,
    int EnemyType,
    int Count);

/// <summary>One four-field <c>[hunt monster]</c> objective from the quest archive.</summary>
public sealed record HuntMonsterRequirement(
    int DungeonId,
    int MinimumDifficulty,
    int MonsterCode,
    int Count);

/// <summary>One verified dungeon clear required by a subtype-6 clear objective.</summary>
public sealed record DungeonClearRequirement(int DungeonId, int Difficulty);

/// <summary>One seven-field <c>[monster reward item]</c> declaration.</summary>
public sealed record QuestMonsterRewardItem(
    int MonsterCode,
    int DungeonId,
    int Difficulty,
    int ItemId,
    int Count,
    int DropRate,
    int MaxStack);

public sealed record QuestDefinition(
    int QuestId,
    string Name,
    QuestGrade? Grade,
    QuestJobRestriction? JobRestriction,
    LevelRange? RequiredLevel,
    int? GrowTypeRequirement,
    int? NpcIndex,
    int? CompleteNpcIndex,
    IReadOnlyList<QuestReward> Rewards,
    string ObjectiveTypeCode,
    string DifficultyCode,
    bool? IsExposedByNpc,
    bool? IsEventOnly,
    int? CreatureKindRequirement,
    int? CreatureLevelRequirement,
    ExpertJobRequirement? ExpertJobRequirement,
    QuestTargetCharacterRestriction? TargetCharacterRestriction,
    int? JobChangeQuestCode,
    IReadOnlyList<IReadOnlyList<int>> CompletedQuestPrerequisiteGroups,
    IReadOnlyList<QuestAnswerRequirement> RequiredQuestAnswers,
    IReadOnlyList<int> CollisionQuestIds,
    IReadOnlyList<int> AccountCollisionQuestIds,
    bool HasSpecialRewardStatus = false,
    IReadOnlyList<SeekingItemRequirement>? SeekingItems = null,
    uint InitialTrigger = 1,
    IReadOnlyList<QuestEventItem>? EventItems = null,
    bool PaysAtCharacterLevel = false,
    IReadOnlyList<HuntEnemyRequirement>? HuntEnemyRequirements = null,
    QuestItemRewardDefinition? ItemRewards = null,
    int GoldMultiplier = 0,
    IReadOnlyList<QuestMonsterRewardItem>? MonsterRewardItems = null,
    IReadOnlyList<HuntMonsterRequirement>? HuntMonsterRequirements = null,
    IReadOnlyList<int>? ClearedQuestRequirements = null,
    DungeonClearRequirement? DungeonClearRequirement = null)
{
    /// <summary>A quest offered regardless of grow type.</summary>
    public const int AnyGrowType = -1;

    public bool AppliesToAnyGrowType => GrowTypeRequirement is null;

    public IReadOnlyList<HuntEnemyRequirement> HuntEnemyRequirements { get; init; }
        = HuntEnemyRequirements ?? [];

    public QuestItemRewardDefinition ItemRewards { get; init; }
        = ItemRewards ?? QuestItemRewardDefinition.Empty;

    public IReadOnlyList<QuestMonsterRewardItem> MonsterRewardItems { get; init; }
        = MonsterRewardItems ?? [];

    public IReadOnlyList<HuntMonsterRequirement> HuntMonsterRequirements { get; init; }
        = HuntMonsterRequirements ?? [];

    public IReadOnlyList<int> ClearedQuestRequirements { get; init; }
        = ClearedQuestRequirements ?? [];

    /// <summary>Whether clearing this quest leaves it available to accept again.</summary>
    /// <remarks>
    /// Read from the grade rather than from a flag of its own, which is where the reference server
    /// reads it: the three repeating grades are the daily pair and the plainly named repeat.
    /// </remarks>
    public bool IsRepeatable => Grade
        is QuestGrade.Daily or QuestGrade.SpecialDaily or QuestGrade.NormallyRepeat;
}

/// <summary>One item a quest hands over at the moment it is accepted.</summary>
/// <remarks>
/// Declared under <c>[depend give item]</c>, which 126 of this archive's quests use. These are not
/// the completion rewards: they arrive with the acceptance so the quest has something to be about,
/// and the acceptance answer names the slot each one landed in.
/// </remarks>
public sealed record QuestEventItem(int ItemId, int Count);

/// <summary>Static quest definitions, keyed by the identifiers the client uses.</summary>
public interface IQuestDefinitionSource
{
    /// <summary>Every quest the content defines, in list order.</summary>
    ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One quest, or null when the content does not define it.</summary>
    ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken);

    /// <summary>Training-quest NPC availability in authoritative level order.</summary>
    ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
        CancellationToken cancellationToken);
}
