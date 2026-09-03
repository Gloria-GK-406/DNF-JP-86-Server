using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.Characters;

/// <summary>How far the gold a character may hold has been raised.</summary>
/// <remarks>
/// Worked out from the two recorded limits rather than stored, so it belongs beside the other
/// rules rather than inside whatever reads those limits.
/// </remarks>
public interface IGoldLimitRuleService
{
    /// <summary>The upgrade level the two limits amount to.</summary>
    int UpgradeLevel(int carryLimit, int auctionLimit);
}

/// <summary>How much gold a character may carry, and what happens when it fills up.</summary>
/// <remarks>
/// Its own service rather than a member of the gold-limit rule beside it, because that one is pure
/// -- it folds two stored numbers into an upgrade level and asks the archive nothing -- while this
/// one reads the archive's own table. Folding them together would make every caller of the pure
/// one supply a content source it has no use for.
/// </remarks>
public interface ICarriedGoldRuleService
{
    /// <summary>
    /// The most gold a character standing at that level may carry.
    /// </summary>
    /// <remarks>
    /// The archive declares a floor per level; the character row declares an upgrade bought on top
    /// of it. The answer is whichever is higher, so a character never carries less than its level
    /// entitles it to and never loses an upgrade it paid for.
    /// <para>
    /// <b>The stored column reads zero for every character today</b> -- nothing in this server has
    /// ever written it -- so in practice the archive's floor is the whole of the answer. Taking the
    /// column alone would clamp every player's gold to nothing.
    /// </para>
    /// <para>
    /// <b>What happens at the cap is decided here rather than at each place gold arrives.</b> An
    /// arrival that would carry a character past the cap fills to it and no further; the remainder
    /// is not held anywhere. The reference server has three different answers to this across its
    /// call sites -- truncate, refuse, and ignore the cap entirely -- which is what happens when
    /// the decision is not in one place.
    /// </para>
    /// </remarks>
    int CarryLimitFor(int characterLevel, int storedCarryLimit);
}

/// <summary>One skill level an expired contract brings down, or removes when it reaches nothing.</summary>
public sealed record ExpertSkillCap(byte Page, short Slot, int? Level);

/// <summary>What an over-skill contract running out does to the levels it was holding up.</summary>
public interface IExpertSkillContractRuleService
{
    /// <summary>
    /// Whether the contracts among these premiums have all run out. A single one still running
    /// means nothing comes down, and no contract at all means there was nothing to run out.
    /// </summary>
    bool HasRunOut(
        IReadOnlyList<(int PremiumType, long EndTimeUnixSeconds)> premiums,
        IReadOnlySet<int> overSkillPremiumTypes,
        DateTimeOffset now);

    /// <summary>The levels that must come down once the contracts have run out.</summary>
    /// <remarks>
    /// The page and slot arrive as they were read, not already narrowed. Narrowing every row would
    /// refuse a row that is out of range even when nothing about it was going to come down; only
    /// the rows that do come down are narrowed, and only then.
    /// </remarks>
    IReadOnlyList<ExpertSkillCap> CapsFor(
        IReadOnlyList<(int Page, int Slot, int SkillId, int Level)> skills,
        IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline);
}

/// <summary>The kinds of persistent effect a dungeon leaves behind.</summary>
public static class DungeonEffectKinds
{
    public const string LuckyStar = "suitable-dungeon-lucky-star";

    public const string SettlementExperience = "settlement-experience-grant";

    public const string SettlementScoreExperience = "settlement-score-experience-adjustment";
}

/// <summary>What a lucky star arriving does to the count already held.</summary>
public sealed record LuckyStarGrant(bool Granted, int NewTotal);

/// <summary>What a settlement carried, as the outbox recorded it.</summary>
public sealed record DungeonExperienceSettlement(
    byte PreviousLevel,
    uint PreviousExp,
    uint RawGain,
    bool NormalizeMaxLevelExp);

/// <summary>How a settlement's gain divides, and what it leaves the character holding.</summary>
public sealed record DungeonExperienceSplit(
    uint HonorGain,
    uint NormalGain,
    int NewLevel,
    ulong NewExp,
    bool NormalizedMaxLevelExp,
    bool Persisted);

/// <summary>What honour arriving leaves the account holding.</summary>
public sealed record DungeonHonourOutcome(
    ulong TotalHonor,
    uint TotalCapsule,
    uint AppliedCapsule);

/// <summary>What a dungeon's persistent effects amount to when they are recovered.</summary>
/// <remarks>
/// These decide; they do not read or write. Everything they need is handed to them, which is what
/// lets the same decisions be checked without a database in reach.
///
/// The experience decision is in two parts on purpose. The account is only consulted when honour
/// was actually produced, and an account that cannot be found is a failure only in that case; a
/// single call taking the account's figures would force the caller to read it every time and turn
/// a missing account into a failure where today there is none.
/// </remarks>
public interface IDungeonEffectRuleService
{
    /// <summary>
    /// Whether a recorded effect is one this server knows how to carry out for this owner. An
    /// effect that is not becomes a dead letter rather than being attempted.
    /// </summary>
    bool CanCarryOut(
        int payloadVersion,
        int recordedAccountId,
        int recordedScope,
        long recordedTarget,
        string kind,
        int accountId,
        int characterId);

    /// <summary>What arriving lucky star does to a count that has a ceiling.</summary>
    LuckyStarGrant GrantLuckyStar(int current);

    /// <summary>How a settlement's gain splits between ordinary experience and honour.</summary>
    DungeonExperienceSplit SplitExperience(
        DungeonExperienceSettlement settlement,
        DungeonExperienceProgressionPolicy policy);

    /// <summary>What that honour adds up to on the account it belongs to.</summary>
    DungeonHonourOutcome AccumulateHonour(
        uint honorGain,
        ulong oldHonor,
        uint oldCapsule,
        DungeonExperienceProgressionPolicy policy);
}
