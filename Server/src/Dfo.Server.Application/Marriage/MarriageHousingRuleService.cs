using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageHousingRuleService(IMarriageHousingContentSource content, INpcShopDefinitionSource shops)
    : IMarriageHousingRuleService
{
    public MarriageHousingContent Content() => content.Load();

    public bool SoldAt(int itemId, int shopId)
    {
        var rules = content.Load();
        var furniture = rules.Furniture.ContainsKey(itemId);
        var ordinary = rules.Seeds.ContainsKey(itemId) || itemId is 10002765 or 10002609 or >= 10002611 and <= 10002615;
        return furniture ? shopId == 1206 : shopId == 1207 && ordinary;
    }

    public ValueTask<NpcShopItemDefinition?> ShopItemAsync(int itemId, CancellationToken ct) => shops.GetItemAsync(itemId, ct);

    public int TreeRewardWeight() => content.Load().Rewards.Sum(x => x.Weight);

    public HousingTreeReward TreeRewardAt(int ticket)
    {
        foreach (var reward in content.Load().Rewards) { if (ticket < reward.Weight) return reward; ticket -= reward.Weight; }
        throw new InvalidOperationException("Invalid tree reward weights.");
    }
}
