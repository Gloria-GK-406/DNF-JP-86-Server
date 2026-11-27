using Dfo.Server.Abstractions.Inventory;

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

    public bool AcceptsMaterial(GuildCompoundInput input, int itemId) => content.GetRarity(input.Kind, itemId) == input.Rarity;

    public int DrawProduct(GuildCompoundRule rule, int materialCount)
    {
        var upgrades = rule.UpgradeBasisPointsPerMaterial * materialCount;
        var pool = upgrades > 0 && random.NextInt64(10_000) < upgrades ? rule.UpgradedProducts : rule.Products;
        return pool[checked((int)random.NextInt64(pool.Count))];
    }
}
