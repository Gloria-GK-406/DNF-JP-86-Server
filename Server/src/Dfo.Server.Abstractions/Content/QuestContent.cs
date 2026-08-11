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
/// the shipping archive. Whether a given character qualifies is a rule, and lives above
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
}

/// <summary>The content's own classification of a quest.</summary>
public enum QuestGrade
{
    Unrecognized = 0,
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
/// The archive's single-letter difficulty code, passed through verbatim. Its meaning is
/// not confirmed, so it is deliberately not given a business name.
/// </param>
/// <param name="ObjectiveTypeCode">
/// The archive's objective-type token, passed through verbatim pending confirmation of
/// the full vocabulary.
/// </param>
public sealed record QuestDefinition(
    int QuestId,
    string Name,
    QuestGrade Grade,
    QuestJobRequirement JobRequirement,
    LevelRange RequiredLevel,
    int GrowTypeRequirement,
    int NpcIndex,
    int CompleteNpcIndex,
    IReadOnlyList<QuestReward> Rewards,
    string ObjectiveTypeCode,
    string DifficultyCode)
{
    /// <summary>A quest offered regardless of grow type.</summary>
    public const int AnyGrowType = -1;

    public bool AppliesToAnyGrowType => GrowTypeRequirement == AnyGrowType;
}

/// <summary>Static quest definitions, keyed by the identifiers the client uses.</summary>
public interface IQuestDefinitionSource
{
    /// <summary>Every quest the content defines, in list order.</summary>
    ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>One quest, or null when the content does not define it.</summary>
    ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken);
}
