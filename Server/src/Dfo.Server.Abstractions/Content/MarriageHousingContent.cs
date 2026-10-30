namespace Dfo.Server.Abstractions.Content;

public sealed record HousingFurnitureRule(int ItemId, int PointCost, IReadOnlySet<int> Slots, bool CanRecall);
public sealed record HousingTreeReward(int ItemId, int Weight, int Count);
public sealed record HousingAttributeRule(int Threshold, IReadOnlyDictionary<string, int> Values);
public sealed record MarriageHousingContent(
    IReadOnlyDictionary<int, HousingFurnitureRule> Furniture,
    IReadOnlyDictionary<int, int> Seeds,
    IReadOnlyDictionary<int, int> Water,
    IReadOnlyList<HousingTreeReward> Rewards,
    IReadOnlyList<HousingAttributeRule> RoomAttributes,
    IReadOnlyList<HousingAttributeRule> RingAttributes)
{
    public IReadOnlySet<(int Town, int Area)> EntrySources { get; init; } = new HashSet<(int, int)>();
}
public interface IMarriageHousingContentSource { MarriageHousingContent Load(); }

/// <summary>What the couple's house is furnished, planted and paid with, as the archive declares it.</summary>
public interface IMarriageHousingRuleService
{
    MarriageHousingContent Content();

    /// <summary>Whether this house shop sells this item at all: furniture from 1206, seeds and supplies from 1207.</summary>
    bool SoldAt(int itemId, int shopId);

    /// <summary>The NPC shop's own declaration of a house item, for its price and materials.</summary>
    ValueTask<NpcShopItemDefinition?> ShopItemAsync(int itemId, CancellationToken ct);

    /// <summary>The tree reward a ticket in <c>[0, total weight)</c> lands on.</summary>
    HousingTreeReward TreeRewardAt(int ticket);

    int TreeRewardWeight();
}
