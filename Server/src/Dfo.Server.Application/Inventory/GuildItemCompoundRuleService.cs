using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuildItemCompoundRuleService(IGuildCompoundContentSource content, ILotteryRandomSource random)
{
    public GuildCompoundRule? Recipe(GuildCompoundInput input)
    {
        if (input.Kind is not (GuildCompoundKind.GuardianGem or GuildCompoundKind.Medal)
            || input.Materials.Count is < 2 or > 4 || input.Rarity is < 0 or > 6)
            return null;

        var rule = content.FindRule(input.Kind, input.Rarity);
        return rule is not null && input.Materials.Count <= rule.MaxMaterials ? rule : null;
    }

    public static bool AcceptsMaterialSlot(GuildCompoundKind kind, BusinessSlot slot, int count) =>
        slot.Index is >= 0 and < 49 && kind switch
        {
            GuildCompoundKind.GuardianGem => slot.Space == BusinessInventorySpace.GuardianGem && count > 0,
            GuildCompoundKind.Medal => slot.Space == BusinessInventorySpace.GuildMedal && count == 1,
            _ => false,
        };

    public bool AcceptsMaterial(GuildCompoundInput input, ItemSnapshot item, DateTimeOffset now)
    {
        var matchesKind = input.Kind switch
        {
            GuildCompoundKind.GuardianGem => item is { LegacyKind: 13, IsPile: true },
            GuildCompoundKind.Medal => item is { LegacyKind: 12, IsPile: false },
            _ => false,
        };
        return matchesKind && !item.IsMirror && item.EquipmentLockId == 0 && item.SortLockFlag == 0
            && !item.Lifetime.IsExpired(now) && content.GetRarity(input.Kind, item.ItemId) == input.Rarity;
    }

    public int DrawProduct(GuildCompoundRule rule, int materialCount)
    {
        var upgrades = rule.UpgradeBasisPointsPerMaterial * materialCount;
        var pool = upgrades > 0 && random.NextInt64(10_000) < upgrades ? rule.UpgradedProducts : rule.Products;
        return pool[checked((int)random.NextInt64(pool.Count))];
    }
}
