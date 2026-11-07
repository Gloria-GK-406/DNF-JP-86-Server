using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>Where one growth-weapon template stands and what its star asks of it.</summary>
/// <param name="MaxExperience">Experience a full star holds; 0 on the last star.</param>
/// <param name="GrowsByExperience">The star advances by filling up, not by a scroll.</param>
/// <param name="NextTemplateId">The template this star turns into; null on the last star.</param>
/// <param name="FatigueThreshold">Fatigue a day must see spent for the experience to keep; 0 when no penalty.</param>
/// <param name="FatiguePenaltyPercent">Share of experience lost after a day below the threshold.</param>
/// <param name="Kill">How kills pay this group; null when the group declares no <c>death</c> acquisition.</param>
public sealed record EquipmentGrowthRule(
    int TemplateId,
    EquipmentGrowthPosition Position,
    int MaxLevel,
    int MaxExperience,
    bool GrowsByExperience,
    int? NextTemplateId,
    int FatigueThreshold,
    int FatiguePenaltyPercent,
    EquipmentGrowthAcquisition? Kill)
{
    public bool IsLastStar => NextTemplateId is null || MaxExperience <= 0 || Position.Star >= MaxLevel;
}

/// <summary>Growth-weapon rules: <c>etc/equipment_grow.etc</c> and the templates' <c>[emancipate]</c> blocks.</summary>
public interface IEquipmentGrowthRuleService
{
    /// <summary>Null when the template is not on a growth line.</summary>
    ValueTask<EquipmentGrowthRule?> ForAsync(int templateId, CancellationToken cancellationToken);

    /// <summary>Experience one kill pays; 0 when the run's kind does not grow weapons.</summary>
    int KillExperience(EquipmentGrowthRule rule, MonsterRank rank, DungeonDifficulty difficulty, DungeonRunKind runKind);

    /// <summary>Experience left after the days that ended since the last settlement.</summary>
    /// <param name="lastDayMaintained">Fatigue spent on the last settled day.</param>
    /// <param name="daysElapsed">Game days between that day and today; 0 when it is today.</param>
    int ExperienceAfterDays(EquipmentGrowthRule rule, int experience, int lastDayMaintained, int daysElapsed);

    /// <summary>What today's maintained fatigue shows as, capped at the star's threshold.</summary>
    byte MaintainedFatigue(EquipmentGrowthRule rule, int consumedToday);
}

public enum EquipmentGrowthChangeKind : byte
{
    /// <summary>Experience changed on the same template (NOTI 0x01E1).</summary>
    Gained = 0,
    /// <summary>The piece turned into its next star (NOTI 0x01E2 type 1).</summary>
    Upgraded = 1,
    /// <summary>Experience and maintained fatigue were rewritten by the daily settlement (NOTI 0x01E2 type 2).</summary>
    Reset = 2,
}

/// <param name="Slot">Where the piece is, as a business slot.</param>
/// <param name="ItemId">The template after the change.</param>
/// <param name="Gained">Experience this change added; 0 for a reset.</param>
public sealed record EquipmentGrowthChange(BusinessSlot Slot, int ItemId, EquipmentGrowthChangeKind Kind,
    ushort Gained, ushort Experience, byte MaintainedFatigue);

/// <summary>The growth of the weapon a character wears.</summary>
/// <remarks>Every call runs inside the caller's write transaction.</remarks>
public interface IEquipmentGrowthDomainService
{
    /// <summary>Credits one kill to the worn growth weapon; null when there is none or nothing changed.</summary>
    ValueTask<EquipmentGrowthChange?> GainFromKillAsync(CharacterId character, MonsterRank rank,
        DungeonDifficulty difficulty, DungeonRunKind runKind, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Applies the daily penalty for the game days that ended since the last settlement.</summary>
    ValueTask<EquipmentGrowthChange?> SettleDaysAsync(CharacterId character, DateTimeOffset at, CancellationToken cancellationToken);
}
