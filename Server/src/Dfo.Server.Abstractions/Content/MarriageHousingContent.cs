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
