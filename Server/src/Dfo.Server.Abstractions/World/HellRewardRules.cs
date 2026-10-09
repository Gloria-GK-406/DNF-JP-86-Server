namespace Dfo.Server.Abstractions.World;

public static class HellRewardRules
{
    // 70 CLuckPoint::GetItemRarity compares cumulative thresholds inclusively.
    public static DropRarity? RarityFor(int roll, IReadOnlyList<int> cumulativeThresholds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(roll);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(roll, 1000000);
        for (var index = 0; index < cumulativeThresholds.Count; index++)
            if (roll <= cumulativeThresholds[index]) return (DropRarity)index;
        return null;
    }
}
