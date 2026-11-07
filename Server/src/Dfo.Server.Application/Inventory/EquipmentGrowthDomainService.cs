using System.Globalization;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

/// <summary>Growth of the worn dragon-leap weapon: kill experience, filling-up stars, daily penalty.</summary>
/// <remarks>
/// The weapon's state lives in the item itself (ItemCore[74] experience, [76] maintained fatigue),
/// so it follows the piece through moves, mail and restarts with no table of its own. Only the
/// worn weapon grows and only the worn weapon is penalised. The last game day settled is the
/// character's <c>equipment_growth_day</c> daily counter.
/// </remarks>
internal sealed class EquipmentGrowthDomainService(IEquipmentDomainService equipments,
    IEquipmentGrowthRuleService rules, ICharacterFatigueDomainService fatigue, ICharacterDailyCounterTable counters,
    IDailyResetRuleService days) : IEquipmentGrowthDomainService
{
    internal const string SettledDayKey = "equipment_growth_day";
    internal static readonly BusinessSlot WeaponSlot = new(BusinessInventorySpace.WornEquipment, 0);

    public async ValueTask<EquipmentGrowthChange?> GainFromKillAsync(CharacterId character, MonsterRank rank,
        DungeonDifficulty difficulty, DungeonRunKind runKind, DateTimeOffset at, CancellationToken ct)
    {
        var settled = await SettleDaysAsync(character, at, ct).ConfigureAwait(false);
        if (await WornAsync(character, ct).ConfigureAwait(false) is not var (weapon, item, rule)) return settled;
        var gain = rules.KillExperience(rule, rank, difficulty, runKind);
        var maintained = await MaintainedTodayAsync(character, rule, at, ct).ConfigureAwait(false);
        var before = item.Snapshot.GrowthExperience;
        var experience = Math.Min(before + gain, Math.Max(rule.MaxExperience, before));
        if (rule.GrowsByExperience && rule.NextTemplateId is { } next && experience >= rule.MaxExperience)
        {
            // A filled automatic star becomes the next star; nothing but the template and the
            // growth state changes, and the overflow does not carry (01E2 hands the client 0).
            var upgraded = await weapon.TransformAsync(next,
                x => x with { GrowthExperience = 0, GrowthMaintainedFatigue = maintained }, ct).ConfigureAwait(false);
            return new(WeaponSlot, upgraded.Snapshot.ItemId, EquipmentGrowthChangeKind.Upgraded,
                checked((ushort)(rule.MaxExperience - before)), 0, maintained);
        }
        if (experience == before && maintained == item.Snapshot.GrowthMaintainedFatigue) return settled;
        var changed = await weapon.SetGrowthAsync(checked((ushort)experience), maintained, ct).ConfigureAwait(false);
        return new(WeaponSlot, changed.Snapshot.ItemId, EquipmentGrowthChangeKind.Gained,
            checked((ushort)(experience - before)), changed.Snapshot.GrowthExperience, maintained);
    }

    public async ValueTask<EquipmentGrowthChange?> SettleDaysAsync(CharacterId character, DateTimeOffset at, CancellationToken ct)
    {
        var today = days.PeriodDay(at);
        var period = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recorded = await counters.FindClaimedPeriodAsync(character, SettledDayKey, ct).ConfigureAwait(false);
        if (recorded == period) return null;
        if (await counters.ClaimAsync(character, SettledDayKey, period, ct).ConfigureAwait(false) != 1)
            throw new InvalidDataException("The growth settlement day was not persisted.");
        if (await WornAsync(character, ct).ConfigureAwait(false) is not var (weapon, item, rule)) return null;
        var experience = (int)item.Snapshot.GrowthExperience;
        if (recorded is not null && DateOnly.TryParseExact(recorded, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var last) && last < today)
        {
            var lastDayMaintained = await fatigue.ConsumedOnAsync(character, last, ct).ConfigureAwait(false);
            experience = rules.ExperienceAfterDays(rule, experience, lastDayMaintained, today.DayNumber - last.DayNumber);
        }
        var maintained = await MaintainedTodayAsync(character, rule, at, ct).ConfigureAwait(false);
        if (experience == item.Snapshot.GrowthExperience && maintained == item.Snapshot.GrowthMaintainedFatigue) return null;
        var changed = await weapon.SetGrowthAsync(checked((ushort)experience), maintained, ct).ConfigureAwait(false);
        return new(WeaponSlot, changed.Snapshot.ItemId, EquipmentGrowthChangeKind.Reset, 0,
            changed.Snapshot.GrowthExperience, maintained);
    }

    private async ValueTask<(IEquipmentInstance Weapon, ItemReference Item, EquipmentGrowthRule Rule)?> WornAsync(
        CharacterId character, CancellationToken ct)
    {
        var weapon = await equipments.Of(character).FindAtAsync(WeaponSlot, ct).ConfigureAwait(false);
        if (weapon is null) return null;
        var item = await weapon.ReadAsync(ct).ConfigureAwait(false);
        return await rules.ForAsync(item.Snapshot.ItemId, ct).ConfigureAwait(false) is { } rule ? (weapon, item, rule) : null;
    }

    private async ValueTask<byte> MaintainedTodayAsync(CharacterId character, EquipmentGrowthRule rule, DateTimeOffset at, CancellationToken ct)
    {
        var balance = await fatigue.BalanceAtAsync(character, at, ct).ConfigureAwait(false);
        return rules.MaintainedFatigue(rule, balance.ConsumedToday);
    }
}
