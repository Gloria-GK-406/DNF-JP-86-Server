using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuildItemCompoundRuleService(IGuildCompoundContentSource content, ILotteryRandomSource random)
{
    public GuildCompoundRule? Recipe(GuildCompoundInput input) =>
        content.FindRule(input.Kind, input.Rarity) is { } rule && input.Materials.Count <= rule.MaxMaterials ? rule : null;

    public bool AcceptsMaterial(GuildCompoundInput input, int itemId) => content.GetRarity(input.Kind, itemId) == input.Rarity;

    public int DrawProduct(GuildCompoundRule rule, int materialCount)
    {
        var upgrades = rule.UpgradeBasisPointsPerMaterial * materialCount;
        var pool = upgrades > 0 && random.NextInt64(10_000) < upgrades ? rule.UpgradedProducts : rule.Products;
        return pool[checked((int)random.NextInt64(pool.Count))];
    }
}
