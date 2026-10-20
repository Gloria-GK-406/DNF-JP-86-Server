using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Characters;

/// <summary>The gold upgrade level the two recorded limits amount to.</summary>
/// <remarks>
/// The lower of the two decides: a character who may carry more than they may auction is held to
/// what they may auction, and the other way round.
/// </remarks>
internal sealed class GoldLimitRuleService : IGoldLimitRuleService
{
    public int UpgradeLevel(int carryLimit, int auctionLimit)
    {
        var value = Math.Min(carryLimit, auctionLimit);
        if (value >= 800_000_000) return 4;
        if (value >= 700_000_000) return 3;
        if (value >= 600_000_000) return 2;
        if (value >= 500_000_000) return 1;
        return 0;
    }
}

/// <summary>How much gold a character may carry.</summary>
/// <remarks>
/// The archive's floor and the stored upgrade are both weighed here rather than at the seam that
/// moves gold, because which of them applies is a rule and that seam is a store. Nothing about the
/// running world is consulted: the level and the stored column are handed in.
/// </remarks>
internal sealed class CarriedGoldRuleService(IGoldCarryLimitSource carryLimits)
    : ICarriedGoldRuleService
{
    public int CarryLimitFor(int characterLevel, int storedCarryLimit) =>
        Math.Max(carryLimits.BaseLimitFor(characterLevel), Math.Max(0, storedCarryLimit));
}

/// <summary>What an over-skill contract running out does to the levels it was holding up.</summary>
internal sealed class ExpertSkillContractRuleService : IExpertSkillContractRuleService
{
    /// <remarks>
    /// A skill the advancement cannot learn at all, and one whose level is fixed rather than
    /// bought, are both left alone. A free grant can hold a level up on its own, so it raises the
    /// ceiling rather than being capped by it. A ceiling of nothing removes the row: the level
    /// only ever existed because of the contract.
    /// </remarks>
    public IReadOnlyList<ExpertSkillCap> CapsFor(
        IReadOnlyList<(int Page, int Slot, int SkillId, int Level)> skills,
        IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(freeBaseline);
        var caps = new List<ExpertSkillCap>();
        foreach (var (page, slot, skillId, level) in skills)
        {
            if (!definitions.TryGetValue(skillId, out var definition) || definition.IsFixedLevel)
            {
                continue;
            }

            if (definition.OrdinaryMaximumLevel is not { } maximum)
            {
                continue;
            }

            if (freeBaseline.TryGetValue(skillId, out var free) && free > maximum)
            {
                maximum = free;
            }

            if (level <= maximum)
            {
                continue;
            }

            caps.Add(new ExpertSkillCap(
                checked((byte)page), checked((short)slot), maximum <= 0 ? null : maximum));
        }

        return caps;
    }
}

/// <summary>What a dungeon's persistent effects amount to when they are recovered.</summary>
internal sealed class DungeonEffectRuleService : IDungeonEffectRuleService
{
    /// <summary>The scope a recorded effect must carry to belong to one character.</summary>
    private const int CharacterScope = 3;

    /// <summary>The only payload version this server knows how to read.</summary>
    private const int KnownPayloadVersion = 1;

    public bool CanCarryOut(
        int payloadVersion,
        int recordedAccountId,
        int recordedScope,
        long recordedTarget,
        string kind,
        int accountId,
        int characterId) =>
        payloadVersion == KnownPayloadVersion
        && recordedAccountId == accountId
        && recordedScope == CharacterScope
        && recordedTarget == characterId
        && kind is DungeonEffectKinds.LuckyStar
            or DungeonEffectKinds.SettlementExperience
            or DungeonEffectKinds.SettlementScoreExperience;

    /// <remarks>
    /// The figures come from the settlement rather than from what was last written down. The
    /// written pair is only a stale-write guard; working from it would throw away everything the
    /// character earned inside the run that has just ended.
    ///
    /// Below the last level, only what overshoots that level's threshold becomes honour. At and
    /// above it, the whole gain does, because there is no level left to raise.
    /// </remarks>
    public DungeonExperienceSplit SplitExperience(
        DungeonExperienceSettlement settlement,
        DungeonExperienceProgressionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(policy);
        var scale = new Dfo.Server.Abstractions.World.CharacterExperienceScale(policy.LevelThresholds);
        var before = scale.ProgressFor(settlement.PreviousExp);
        var total = (ulong)before.Experience + settlement.RawGain;
        var next = scale.ProgressFor(checked((long)total));
        var honor = checked((uint)(total - next.Experience));
        var normalized = settlement.PreviousExp > scale.MaximumExperience;
        return new DungeonExperienceSplit(honor, settlement.RawGain - honor, next.Level, next.Experience,
            normalized, next.Level != settlement.PreviousLevel || next.Experience != settlement.PreviousExp);
    }

    /// <remarks>
    /// Any honour at all earns at least one capsule while the rate is above nothing, so a small
    /// gain is not rounded away to no reward.
    /// </remarks>
    public DungeonHonourOutcome AccumulateHonour(
        uint honorGain,
        ulong oldHonor,
        uint oldCapsule,
        DungeonExperienceProgressionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var totalHonor = Math.Min(policy.MaximumHonorExperience, Math.Min(oldHonor, policy.MaximumHonorExperience) + honorGain);
        var capsuleGain = checked((uint)Math.Min(uint.MaxValue,
            Math.Max(honorGain > 0 && policy.GrowthCapsuleRatePercent > 0 ? 1UL : 0UL,
                (ulong)honorGain * (uint)policy.GrowthCapsuleRatePercent / 100UL)));
        var totalCapsule = checked((uint)Math.Min(policy.GrowthCapsuleMaximum, (ulong)oldCapsule + capsuleGain));
        return new DungeonHonourOutcome(
            totalHonor, totalCapsule, totalCapsule - Math.Min(oldCapsule, policy.GrowthCapsuleMaximum));
    }
}
