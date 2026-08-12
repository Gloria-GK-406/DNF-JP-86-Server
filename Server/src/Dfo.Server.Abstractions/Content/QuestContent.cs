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

/// <summary>A quest restriction on one expert-job type and its minimum level.</summary>
public sealed record ExpertJobRequirement(int ExpertJobType, int MinimumLevel);

/// <summary>A required answer to a prior question quest.</summary>
public sealed record QuestAnswerRequirement(int QuestId, int AnswerIndex);

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
/// <param name="ObjectiveTypeCode">
/// What the quest asks the character to do, as a bare vocabulary value such as
/// <c>seeking</c> or <c>meet npc</c>. It is passed through rather than modelled as an
/// enumeration because the full vocabulary is not yet confirmed.
/// </param>
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
    bool HasSpecialRewardStatus = false)
{
    /// <summary>A quest offered regardless of grow type.</summary>
    public const int AnyGrowType = -1;

    public bool AppliesToAnyGrowType => GrowTypeRequirement is null;
}

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
